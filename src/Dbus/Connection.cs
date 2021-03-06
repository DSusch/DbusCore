﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dbus
{
    public partial class Connection : IDisposable
    {
        public const string SystemBusAddress = "unix:path=/var/run/dbus/system_bus_socket";

        private readonly IntPtr socketHandle;
        private readonly Stream stream;
        private readonly ConcurrentDictionary<uint, TaskCompletionSource<ReceivedMethodReturn>> expectedMessages;
        private readonly ConcurrentDictionary<string, Action<MessageHeader, byte[]>> signalHandlers;
        private readonly ConcurrentDictionary<string, Func<uint, MessageHeader, byte[], Task>> objectProxies;
        private readonly CancellationTokenSource receiveCts;
        private readonly Task receiveTask;

        private int serialCounter;
        private IOrgFreedesktopDbus orgFreedesktopDbus;

        private Connection(IntPtr socketHandle, Stream stream)
        {
            this.socketHandle = socketHandle;
            this.stream = stream;

            expectedMessages = new ConcurrentDictionary<uint, TaskCompletionSource<ReceivedMethodReturn>>();
            signalHandlers = new ConcurrentDictionary<string, Action<MessageHeader, byte[]>>();
            objectProxies = new ConcurrentDictionary<string, Func<uint, MessageHeader, byte[], Task>>();
            receiveCts = new CancellationTokenSource();

            receiveTask = Task.Factory.StartNew(
                receive,
                receiveCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
        }

        public async static Task<Connection> CreateAsync(DbusConnectionOptions options)
        {
            var endPoint = EndPointFactory.Create(options.Address);
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, 0);
            await socket.ConnectAsync(endPoint).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);

            await authenticate(stream).ConfigureAwait(false);

            var socketHandle = getSocketHandle(socket);
            var result = new Connection(socketHandle, stream);

            try
            {
                var orgFreedesktopDbus = result.Consume<IOrgFreedesktopDbus>();
                result.orgFreedesktopDbus = orgFreedesktopDbus;
                await orgFreedesktopDbus.HelloAsync().ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidOperationException("Could not find the generated implementation of 'IOrgFreedesktopDbus'. Did you run the DoInit method of the generated code?");
            }

            return result;
        }

        // TODO: Only support netstandard 2.0 and beyond
        private static IntPtr getSocketHandle(Socket socket)
        {
            // netstandard up until 1.6 doesn't provide the Handle property
            // or any other way to get the raw socket handle.
            // Use reflection...
            var type = socket.GetType().GetTypeInfo();
            var property = type.GetProperty("Handle");
            if (property != null)
                // ... to access the existing property on full framework...
                return (IntPtr)property.GetValue(socket);
            else
            {
                // ... or access the private(!) field of the .NET Core's implementation
                var field = type.GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic);
                return ((SafeHandle)field.GetValue(socket)).DangerousGetHandle();
            }
        }

        public IDisposable RegisterObjectProxy(
            ObjectPath path,
            string interfaceName,
            Func<uint, MessageHeader, byte[], Task> proxy
        )
        {
            var dictionaryEntry = path + "\0" + interfaceName;
            if (!objectProxies.TryAdd(dictionaryEntry, proxy))
                throw new InvalidOperationException("Attempted to register an object proxy twice");

            return new deregistration
            {
                Deregister = () =>
                {
                    Func<uint, MessageHeader, byte[], Task> _;
                    objectProxies.TryRemove(dictionaryEntry, out _);
                }
            };
        }

        public IDisposable RegisterSignalHandler(
            ObjectPath path,
            string interfaceName,
            string member,
            Action<MessageHeader, byte[]> handler
        )
        {
            var dictionaryEntry = path + "\0" + interfaceName + "\0" + member;
            signalHandlers.AddOrUpdate(
                dictionaryEntry,
                handler,
                (_, existingHandler) => existingHandler + handler
            );

            var match = $"type='signal',interface='{interfaceName}',member={member},path='{path}'";
            var canRegister = orgFreedesktopDbus != null;
            if (canRegister)
                Task.Run(() => orgFreedesktopDbus.AddMatchAsync(match));

            return new deregistration
            {
                Deregister = () =>
                {
                    if (canRegister)
                        Task.Run(() => orgFreedesktopDbus.RemoveMatchAsync(match));
                    Action<MessageHeader, byte[]> current;
                    do
                    {
                        signalHandlers.TryGetValue(dictionaryEntry, out current);
                    } while (!signalHandlers.TryUpdate(dictionaryEntry, current - handler, current));
                }
            };
        }

        private static void AddHeader(List<byte> buffer, ref int index, ObjectPath path)
        {
            Encoder.EnsureAlignment(buffer, ref index, 8);
            Encoder.Add(buffer, ref index, (byte)1);
            Encoder.AddVariant(buffer, ref index, path);
        }

        private static void AddHeader(List<byte> buffer, ref int index, uint replySerial)
        {
            Encoder.EnsureAlignment(buffer, ref index, 8);
            Encoder.Add(buffer, ref index, (byte)5);
            Encoder.AddVariant(buffer, ref index, replySerial);
        }

        private static void AddHeader(List<byte> buffer, ref int index, Signature signature)
        {
            Encoder.EnsureAlignment(buffer, ref index, 8);
            Encoder.Add(buffer, ref index, (byte)8);
            Encoder.AddVariant(buffer, ref index, signature);
        }

        private static void AddHeader(List<byte> buffer, ref int index, byte type, string value)
        {
            Encoder.EnsureAlignment(buffer, ref index, 8);
            Encoder.Add(buffer, ref index, type);
            Encoder.AddVariant(buffer, ref index, value);
        }

        public async Task<ReceivedMethodReturn> SendMethodCall(
            ObjectPath path,
            string interfaceName,
            string methodName,
            string destination,
            List<byte> body,
            Signature signature
        )
        {
            var serial = Interlocked.Increment(ref serialCounter);

            var message = Encoder.StartNew();
            var index = 0;
            Encoder.Add(message, ref index, (byte)'l'); // little endian
            Encoder.Add(message, ref index, (byte)1); // method call
            Encoder.Add(message, ref index, (byte)0); // flags
            Encoder.Add(message, ref index, (byte)1); // protocol version
            Encoder.Add(message, ref index, body.Count); // Actually uint
            Encoder.Add(message, ref index, serial); // Actually uint

            Encoder.AddArray(message, ref index, (List<byte> buffer, ref int localIndex) =>
            {
                AddHeader(buffer, ref localIndex, path);
                AddHeader(buffer, ref localIndex, 2, interfaceName);
                AddHeader(buffer, ref localIndex, 3, methodName);
                AddHeader(buffer, ref localIndex, 6, destination);
                if (body.Count > 0)
                    AddHeader(buffer, ref localIndex, signature);
            });
            Encoder.EnsureAlignment(message, ref index, 8);
            message.AddRange(body);

            var tcs = new TaskCompletionSource<ReceivedMethodReturn>();
            expectedMessages[(uint)serial] = tcs;

            var messageArray = message.ToArray();
            await stream.WriteAsync(messageArray, 0, messageArray.Length).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }

        public async Task SendMethodReturnAsync(uint replySerial, string destination, List<byte> body, Signature signature)
        {
            var serial = Interlocked.Increment(ref serialCounter);

            var message = Encoder.StartNew();
            var index = 0;
            Encoder.Add(message, ref index, (byte)'l'); // little endian
            Encoder.Add(message, ref index, (byte)2); // method return
            Encoder.Add(message, ref index, (byte)1); // no reply expected
            Encoder.Add(message, ref index, (byte)1); // protocol version
            Encoder.Add(message, ref index, body.Count); // Actually uint
            Encoder.Add(message, ref index, serial); // Actually uint

            Encoder.AddArray(message, ref index, (List<byte> buffer, ref int localIndex) =>
            {
                AddHeader(buffer, ref localIndex, 6, destination);
                AddHeader(buffer, ref localIndex, replySerial);
                if (body.Count > 0)
                    AddHeader(buffer, ref localIndex, signature);
            });
            Encoder.EnsureAlignment(message, ref index, 8);
            message.AddRange(body);

            var messageArray = message.ToArray();
            await stream.WriteAsync(messageArray, 0, messageArray.Length).ConfigureAwait(false);
        }

        private async Task sendMethodCallErrorAsync(uint replySerial, string destination, string error, string errorMessage)
        {
            var serial = Interlocked.Increment(ref serialCounter);

            var index = 0;
            var body = Encoder.StartNew();
            Encoder.Add(body, ref index, errorMessage);

            var message = Encoder.StartNew();
            index = 0;
            Encoder.Add(message, ref index, (byte)'l'); // little endian
            Encoder.Add(message, ref index, (byte)3); // error
            Encoder.Add(message, ref index, (byte)1); // no reply expected
            Encoder.Add(message, ref index, (byte)1); // protocol version
            Encoder.Add(message, ref index, body.Count); // Actually uint
            Encoder.Add(message, ref index, serial); // Actually uint

            Encoder.AddArray(message, ref index, (List<byte> buffer, ref int localIndex) =>
            {
                AddHeader(buffer, ref localIndex, 6, destination);
                AddHeader(buffer, ref localIndex, 4, error);
                AddHeader(buffer, ref localIndex, replySerial);
                if (body.Count > 0)
                    AddHeader(buffer, ref localIndex, (Signature)"s");
            });
            Encoder.EnsureAlignment(message, ref index, 8);
            message.AddRange(body);

            var messageArray = message.ToArray();
            await stream.WriteAsync(messageArray, 0, messageArray.Length).ConfigureAwait(false);
        }

        [DllImport("libc")]
        private static extern int recvmsg(IntPtr sockfd, [In] ref msghdr buf, int flags);

        private unsafe struct iovec
        {
            public byte* iov_base;
            public int iov_len;
        }

        private unsafe struct msghdr
        {
            public IntPtr name;
            public int namelen;
            public iovec* iov;
            public int iovlen;
            public int[] control;
            public int controllen;
            public int flags;
        }

        private unsafe void receive()
        {
            var fixedLengthHeader = new byte[16]; // header up until the array length
            var token = receiveCts.Token;
            var control = new int[16];

            var hasValidFixedHeader = false;

            while (!token.IsCancellationRequested)
            {
                if (!hasValidFixedHeader)
                    fixed (byte* fixedLengthHeaderP = fixedLengthHeader)
                    {
                        var iovecs = stackalloc iovec[1];
                        iovecs[0].iov_base = fixedLengthHeaderP;
                        iovecs[0].iov_len = 16;

                        var msg = new msghdr();
                        msg.iov = iovecs;
                        msg.iovlen = 1;
                        msg.controllen = control.Length * sizeof(int);
                        msg.control = control;
                        if (recvmsg(socketHandle, ref msg, 0) <= 0)
                            return;
                    }

                var index = 0;
                var endianess = Decoder.GetByte(fixedLengthHeader, ref index);
                if (endianess != (byte)'l')
                    throw new InvalidDataException("Wrong endianess");
                var messageType = Decoder.GetByte(fixedLengthHeader, ref index);
                Decoder.GetByte(fixedLengthHeader, ref index);
                var protocolVersion = Decoder.GetByte(fixedLengthHeader, ref index);
                if (protocolVersion != 1)
                    throw new InvalidDataException("Wrong protocol version");
                var bodyLength = Decoder.GetInt32(fixedLengthHeader, ref index); // Actually uint
                var receivedSerial = Decoder.GetUInt32(fixedLengthHeader, ref index);
                var receivedArrayLength = Decoder.GetInt32(fixedLengthHeader, ref index); // Actually uint
                Alignment.Advance(ref receivedArrayLength, 8);
                var headerBytes = new byte[receivedArrayLength];
                var bodyBytes = new byte[bodyLength];

                fixed (byte* headerP = headerBytes)
                fixed (byte* bodyP = bodyBytes)
                fixed (byte* fixedLengthHeaderP = fixedLengthHeader)
                {
                    var iovecs = stackalloc iovec[3];
                    iovecs[0].iov_base = headerP;
                    iovecs[0].iov_len = receivedArrayLength;
                    iovecs[1].iov_base = bodyP;
                    iovecs[1].iov_len = bodyLength;
                    iovecs[2].iov_base = fixedLengthHeaderP;
                    iovecs[2].iov_len = 16;
                    var nextMsg = new msghdr
                    {
                        iov = iovecs,
                        iovlen = 3,
                        control = control,
                        controllen = control.Length * sizeof(int),
                    };
                    var len = recvmsg(socketHandle, ref nextMsg, 0);
                    if (len <= 0)
                        return;
                    hasValidFixedHeader = len == receivedArrayLength + bodyLength + fixedLengthHeader.Length;
                }

                var header = new MessageHeader(headerBytes, control);

                switch (messageType)
                {
                    case 1:
                        handleMethodCall(receivedSerial, header, bodyBytes);
                        break;
                    case 2:
                        handleMethodReturn(header, bodyBytes);
                        break;
                    case 3:
                        handleError(header, bodyBytes);
                        break;
                    case 4:
                        handleSignal(header, bodyBytes);
                        break;
                }
            }
        }

        private void handleMethodCall(uint replySerial, MessageHeader header, byte[] body)
        {
            var dictionaryEntry = header.Path + "\0" + header.InterfaceName;
            Func<uint, MessageHeader, byte[], Task> proxy;
            if (objectProxies.TryGetValue(dictionaryEntry, out proxy))
                Task.Run(() =>
                {
                    try
                    {
                        return proxy(replySerial, header, body);
                    }
                    catch (DbusException dbusException)
                    {
                        return sendMethodCallErrorAsync(
                            replySerial,
                            header.Sender,
                            dbusException.ErrorName,
                            dbusException.ErrorMessage
                         );
                    }
                    catch (Exception e)
                    {
                        return sendMethodCallErrorAsync(
                            replySerial,
                            header.Sender,
                            DbusException.CreateErrorName("General"),
                            e.Message
                         );
                    }
                });
            else
                Task.Run(() => sendMethodCallErrorAsync(
                    replySerial,
                    header.Sender,
                    DbusException.CreateErrorName("MethodCallTargetNotFound"),
                    "The requested method call isn't mapped to an actual object"
                ));
        }

        private void handleMethodReturn(
            MessageHeader header,
            byte[] body
        )
        {
            TaskCompletionSource<ReceivedMethodReturn> tcs;
            if (!expectedMessages.TryRemove(header.ReplySerial, out tcs))
                throw new InvalidOperationException("Couldn't find the method call for the method return");
            var receivedMessage = new ReceivedMethodReturn
            {
                Header = header,
                Body = body,
                Signature = header.BodySignature,
            };

            Task.Run(() => tcs.SetResult(receivedMessage));
        }

        private void handleError(MessageHeader header, byte[] body)
        {
            if (header.ReplySerial == 0)
                throw new InvalidOperationException("Only errors for method calls are supported");
            if (!header.BodySignature.ToString().StartsWith("s"))
                throw new InvalidOperationException("Errors are expected to start their body with a string");

            TaskCompletionSource<ReceivedMethodReturn> tcs;
            if (!expectedMessages.TryRemove(header.ReplySerial, out tcs))
                throw new InvalidOperationException("Couldn't find the method call for the error");

            var index = 0;
            var message = Decoder.GetString(body, ref index);
            var exception = new DbusException(header.ErrorName, message);
            Task.Run(() => tcs.SetException(exception));
        }

        private void handleSignal(
            MessageHeader header,
            byte[] body
        )
        {
            var dictionaryEntry = header.Path + "\0" + header.InterfaceName + "\0" + header.Member;
            Action<MessageHeader, byte[]> handler;
            if (signalHandlers.TryGetValue(dictionaryEntry, out handler))
                Task.Run(() => handler(header, body));
        }

        public void Dispose()
        {
            orgFreedesktopDbus.Dispose();
            receiveCts.Cancel();
            stream.Dispose();
            receiveTask.Wait();
        }

        private class deregistration : IDisposable
        {
            public Action Deregister;

            public void Dispose()
            {
                Deregister();
            }
        }
    }
}
