﻿using System;

namespace Dbus
{
    public class ObjectPath
    {
        private readonly string objectPath = "";

        private ObjectPath(string objectPath)
        {
            if (objectPath == null)
                throw new ArgumentNullException(nameof(objectPath));
            this.objectPath = objectPath;
        }

        public static implicit operator ObjectPath(string objectPath) =>
            new ObjectPath(objectPath);

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            var other = (ObjectPath)obj;
            return objectPath == other.objectPath;
        }

        public override int GetHashCode() => objectPath.GetHashCode();
        public override string ToString() => objectPath;
        public static bool operator ==(ObjectPath lhs, ObjectPath rhs)
        {
            if (ReferenceEquals(lhs, rhs))
                return true;
            if ((object)lhs == null || (object)rhs == null)
                return false;
            return lhs.objectPath == rhs.objectPath;
        }
        public static bool operator !=(ObjectPath lhs, ObjectPath rhs) => !(lhs == rhs);
    }
}
