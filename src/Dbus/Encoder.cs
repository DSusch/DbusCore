﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Dbus
{
    public static class Encoder
    {
        public delegate void ElementWriter(List<byte> buffer, ref int index);

        public static List<byte> StartNew()
        {
            return new List<byte>();
        }

        public static void Add(List<byte> buffer, ref int index, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Add(buffer, ref index, bytes.Length); // Actually uint
            buffer.AddRange(bytes);
            index += bytes.Length;
            buffer.Add(0);
            index += 1;
        }

        public static void Add(List<byte> buffer, ref int index, Signature signature)
        {
            var bytes = Encoding.UTF8.GetBytes(signature.ToString());
            Add(buffer, ref index, (byte)bytes.Length);
            buffer.AddRange(bytes);
            index += bytes.Length;
            buffer.Add(0);
            index += 1;
        }

        public static void Add(List<byte> buffer, ref int index, ObjectPath value)
        {
            Add(buffer, ref index, value.ToString());
        }

        public static void Add(List<byte> buffer, ref int index, short value)
        {
            EnsureAlignment(buffer, ref index, 2);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 2;
        }

        public static void Add(List<byte> buffer, ref int index, ushort value)
        {
            EnsureAlignment(buffer, ref index, 2);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 2;
        }

        public static void Add(List<byte> buffer, ref int index, int value)
        {
            EnsureAlignment(buffer, ref index, 4);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 4;
        }

        public static void Add(List<byte> buffer, ref int index, uint value)
        {
            EnsureAlignment(buffer, ref index, 4);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 4;
        }

        public static void Add(List<byte> buffer, ref int index, ulong value)
        {
            EnsureAlignment(buffer, ref index, 8);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 8;
        }

        public static void Add(List<byte> buffer, ref int index, long value)
        {
            EnsureAlignment(buffer, ref index, 8);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 8;
        }

        public static void Add(List<byte> buffer, ref int index, double value)
        {
            EnsureAlignment(buffer, ref index, 8);
            buffer.AddRange(BitConverter.GetBytes(value));
            index += 8;
        }

        public static void Add(List<byte> buffer, ref int index, byte value)
        {
            buffer.Add(value);
            index += 1;
        }

        public static void Add(List<byte> buffer, ref int index, bool value)
        {
            Add(buffer, ref index, value ? 1 : 0);
        }

        public static void AddArray(List<byte> buffer, ref int index, ElementWriter writer)
        {
            var lengthPosition = index;
            Add(buffer, ref index, 0); // Actually uint
            writer(buffer, ref index);
            var arrayLength = index - (lengthPosition + 4);
            var lengthBytes = BitConverter.GetBytes(arrayLength);
            for (var i = 0; i < 4; ++i)
                buffer[lengthPosition + i] = lengthBytes[i];
        }

        public static void AddVariant(List<byte> buffer, ref int index, string value)
        {
            Add(buffer, ref index, (Signature)"s");
            Add(buffer, ref index, value);
        }

        public static void AddVariant(List<byte> buffer, ref int index, ObjectPath value)
        {
            Add(buffer, ref index, (Signature)"o");
            Add(buffer, ref index, value.ToString());
        }

        public static void AddVariant(List<byte> buffer, ref int index, uint value)
        {
            Add(buffer, ref index, (Signature)"u");
            Add(buffer, ref index, value);
        }

        public static void AddVariant(List<byte> buffer, ref int index, Signature signature)
        {
            Add(buffer, ref index, (Signature)"g");
            Add(buffer, ref index, signature);
        }

        public static void EnsureAlignment(List<byte> buffer, ref int index, int alignment)
        {
            var bytesToAdd = Alignment.Calculate(buffer.Count, alignment);
            for (var i = 0; i < bytesToAdd; ++i)
                buffer.Add(0);
            index += bytesToAdd;
        }
    }
}
