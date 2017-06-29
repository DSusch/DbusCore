﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dbus
{
    public class OrgFreedesktopDbusObjectManager : IOrgFreedesktopDbusObjectManagerProvide
    {

        public ObjectPath Root { get; }

        private readonly Connection connection;
        private readonly Dictionary<ObjectPath, List<IProxy>> managedObjects;
        //        private Dictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>> managedObjects;

        public OrgFreedesktopDbusObjectManager(Connection connection, ObjectPath root)
        {
            this.connection = connection;
            managedObjects = new Dictionary<ObjectPath, List<IProxy>>() { };
            Root = root;
        }

        public event Action<ObjectPath, IDictionary<string, IDictionary<string, object>>> InterfacesAdded;
        public event Action<ObjectPath, IEnumerable<string>> InterfacesRemoved;

        public void AddObject<TInterface, TImplementation>(TImplementation instance, ObjectPath path) where TImplementation : TInterface
        {
            var fullPath = buildFullPath(path);
            var proxy = (IProxy)connection.Publish<TInterface>(instance, fullPath);
            if (managedObjects.ContainsKey(fullPath))
            {
                managedObjects[fullPath].Add((proxy));
            }
            else
            {
                managedObjects.Add(fullPath, new List<IProxy>() { (proxy) });
            }
        }

        private string buildFullPath(ObjectPath path)
        {
            if (!path.ToString().StartsWith("./"))
                throw new ArgumentException("A partial path has to start with ./");
            else
            {
                if (Root == "/")
                {
                    return path.ToString().Substring(1);
                }
                else
                {
                    if (path.ToString() == "./")
                        return Root.ToString();
                    else
                        return Root + path.ToString().Substring(1);
                }
            }
        }

        public async Task<Dictionary<ObjectPath, List<IProxy>>> GetManagedObjectsAsync()
        {
            await Task.Delay(1000);
            return managedObjects;
        }

        public void Dispose()
        {
            foreach (var proxies in managedObjects)
            {
                foreach (var proxy in proxies.Value)
                {
                    proxy.Dispose();
                }
            }
        }
    }
}