namespace Dbus.Sample
{

    public sealed class OrgFreedesktopDbus : Dbus.IOrgFreedesktopDbus
    {
        private readonly Connection connection;
        private readonly ObjectPath path;
        private readonly string destination;
        private readonly System.Collections.Generic.List<System.IDisposable> eventSubscriptions = new System.Collections.Generic.List<System.IDisposable>();

        public OrgFreedesktopDbus(Connection connection, ObjectPath path = null, string destination = null)
        {
            this.connection = connection;
            this.path = path ?? "/org/freedesktop/DBus";
            this.destination = destination ?? "org.freedesktop.DBus";
            eventSubscriptions.Add(connection.RegisterSignalHandler(
                path,
                "org.freedesktop.DBus",
                "NameAcquired",
                handleNameAcquired
            ));

        }

        public async System.Threading.Tasks.Task<System.String> HelloAsync()
        {
            var sendBody = Encoder.StartNew();

            var receivedMessage = await connection.SendMethodCall(
                path,
                "org.freedesktop.DBus",
                "Hello",
                destination,
                sendBody,
                ""
            );
            assertSignature(receivedMessage.Signature, "s");
            var index = 0;
            var result = Decoder.GetString(receivedMessage.Body, ref index);
            return result;

        }

        public async System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<System.String>> ListNamesAsync()
        {
            var sendBody = Encoder.StartNew();

            var receivedMessage = await connection.SendMethodCall(
                path,
                "org.freedesktop.DBus",
                "ListNames",
                destination,
                sendBody,
                ""
            );
            assertSignature(receivedMessage.Signature, "as");
            var index = 0;
            var result = Decoder.GetArray(receivedMessage.Body, ref index, Decoder.GetString);
            return result;

        }

        public async System.Threading.Tasks.Task<System.UInt32> RequestNameAsync(System.String name, System.UInt32 flags)
        {
            var sendBody = Encoder.StartNew();
            var sendIndex = 0;
            Encoder.Add(sendBody, ref sendIndex, name);
            Encoder.Add(sendBody, ref sendIndex, flags);

            var receivedMessage = await connection.SendMethodCall(
                path,
                "org.freedesktop.DBus",
                "RequestName",
                destination,
                sendBody,
                "su"
            );
            assertSignature(receivedMessage.Signature, "u");
            var index = 0;
            var result = Decoder.GetUInt32(receivedMessage.Body, ref index);
            return result;

        }

        public event System.Action<System.String> NameAcquired;
        private void handleNameAcquired(MessageHeader header, byte[] body)
        {
            assertSignature(header.BodySignature, "s");
            var index = 0;
            var decoded = Decoder.GetString(body, ref index);
            NameAcquired?.Invoke(decoded);
        }

        private static void assertSignature(Signature actual, Signature expected)
        {
            if (actual != expected)
                throw new System.InvalidOperationException($"Unexpected signature. Got ${ actual}, but expected ${ expected}");
        }

        public void Dispose()
        {
            eventSubscriptions.ForEach(x => x.Dispose());
        }
    }

    public sealed class OrgFreedesktopUpower : Dbus.Sample.IOrgFreedesktopUpower
    {
        private readonly Connection connection;
        private readonly ObjectPath path;
        private readonly string destination;
        private readonly System.Collections.Generic.List<System.IDisposable> eventSubscriptions = new System.Collections.Generic.List<System.IDisposable>();

        public OrgFreedesktopUpower(Connection connection, ObjectPath path = null, string destination = null)
        {
            this.connection = connection;
            this.path = path ?? "/org/freedesktop/UPower";
            this.destination = destination ?? "org.freedesktop.UPower";

        }

        public async System.Threading.Tasks.Task<System.Collections.Generic.IDictionary<System.String,System.Object>> GetAllAsync(System.String interfaceName)
        {
            var sendBody = Encoder.StartNew();
            var sendIndex = 0;
            Encoder.Add(sendBody, ref sendIndex, interfaceName);

            var receivedMessage = await connection.SendMethodCall(
                path,
                "org.freedesktop.UPower",
                "GetAll",
                destination,
                sendBody,
                "s"
            );
            assertSignature(receivedMessage.Signature, "a{sv}");
            var index = 0;
            var result = Decoder.GetDictionary(receivedMessage.Body, ref index, Decoder.GetString, Decoder.GetObject);
            return result;

        }


        private static void assertSignature(Signature actual, Signature expected)
        {
            if (actual != expected)
                throw new System.InvalidOperationException($"Unexpected signature. Got ${ actual}, but expected ${ expected}");
        }

        public void Dispose()
        {
            eventSubscriptions.ForEach(x => x.Dispose());
        }
    }

    public sealed class SampleObject_Proxy: System.IDisposable
    {
        private readonly Dbus.Connection connection;
        private readonly Dbus.Sample.SampleObject target;

        private System.IDisposable registration;

        public SampleObject_Proxy(Dbus.Connection connection, Dbus.Sample.SampleObject target, Dbus.ObjectPath path = default(Dbus.ObjectPath))
        {
            this.connection = connection;
            this.target = target;
            registration = connection.RegisterObjectProxy(
                path ?? "/org/dbuscore/sample",
                "org.dbuscore.sample.interface",
                handleMethodCall
            );
        }

        private System.Threading.Tasks.Task handleMethodCall(uint replySerial, Dbus.MessageHeader header, byte[] body)
        {
            switch (header.Member)
            {
                case "MyComplexMethod":
                    return handleMyComplexMethodAsync(replySerial, header, body);
                case "MyEcho":
                    return handleMyEchoAsync(replySerial, header, body);
                case "MyVoid":
                    return handleMyVoidAsync(replySerial, header, body);
                default:
                    throw new DbusException(
                        DbusException.CreateErrorName("UnknownMethod"),
                        "Method not supported"
                    );
            }
        }

        private async System.Threading.Tasks.Task handleMyComplexMethodAsync(uint replySerial, Dbus.MessageHeader header, byte[] receivedBody)
        {
            assertSignature(header.BodySignature, "sii");
            var receiveIndex = 0;
            var p1 = Decoder.GetString(receivedBody, ref receiveIndex);
            var p2 = Decoder.GetInt32(receivedBody, ref receiveIndex);
            var p3 = Decoder.GetInt32(receivedBody, ref receiveIndex);
            var result = await target.MyComplexMethodAsync(p1, p2, p3);
            var sendBody = Encoder.StartNew();
            var sendIndex = 0;
            Encoder.Add(sendBody, ref sendIndex, result.Item1);
            Encoder.Add(sendBody, ref sendIndex, result.Item2);
            await connection.SendMethodReturnAsync(replySerial, header.Sender, sendBody,"si");
        }

        private async System.Threading.Tasks.Task handleMyEchoAsync(uint replySerial, Dbus.MessageHeader header, byte[] receivedBody)
        {
            assertSignature(header.BodySignature, "s");
            var receiveIndex = 0;
            var message = Decoder.GetString(receivedBody, ref receiveIndex);
            var result = await target.MyEchoAsync(message);
            var sendBody = Encoder.StartNew();
            var sendIndex = 0;
            Encoder.Add(sendBody, ref sendIndex, result);
            await connection.SendMethodReturnAsync(replySerial, header.Sender, sendBody,"s");
        }

        private async System.Threading.Tasks.Task handleMyVoidAsync(uint replySerial, Dbus.MessageHeader header, byte[] receivedBody)
        {
            assertSignature(header.BodySignature, "");
            await target.MyVoidAsync();
            var sendBody = Encoder.StartNew();
            await connection.SendMethodReturnAsync(replySerial, header.Sender, sendBody,"");
        }


        private static void assertSignature(Signature actual, Signature expected)
        {
            if (actual != expected)
                throw new DbusException(
                    DbusException.CreateErrorName("InvalidSignature"),
                    "Invalid signature"
                );
        }

        public void Dispose()
        {
            registration.Dispose();
        }
    }

}
