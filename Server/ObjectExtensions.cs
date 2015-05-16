using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web;

namespace NuGet.Server
{
    public static class ObjectExtensions
    {
        public static T ToObject<T>(this IDictionary<string, object> source)
            where T : class, new()
        {
            T someObject = new T();
            Type someObjectType = someObject.GetType();

            foreach (KeyValuePair<string, object> item in source)
            {
                var obj = someObjectType.GetProperty(item.Key);
                if (obj != null)
                {
                    if (obj.PropertyType == typeof(NuGet.SemanticVersion))
                    {
                        var dictionary = ((Dictionary<string, object>)item.Value);
                        var dictionaryVersion = ((Dictionary<string, object>)dictionary["Version"]);
                        var version = GetVersion(dictionaryVersion);
                        var ver = new NuGet.SemanticVersion(version, dictionary["SpecialVersion"].ToString());
                        obj.SetValue(someObject, ver, null);
                    }
                    else if (obj.PropertyType == typeof(Version))
                    {
                        var versionDict = ((Dictionary<string, object>)item.Value);
                        var version = GetVersion(versionDict);
                        obj.SetValue(someObject, version, null);
                    }
                    else if (obj.PropertyType == typeof(IEnumerable<NuGet.PackageDependencySet>))
                    {
                        var list = ((IEnumerable<object>)item.Value).Cast<IEnumerable<NuGet.PackageDependencySet>>();
                        if (list.Any())
                        {
                            obj.SetValue(someObject, list, null);
                        }
                    }
                    else if (obj.PropertyType == typeof(IEnumerable<NuGet.FrameworkAssemblyReference>))
                    {
                        var list = ((IEnumerable<object>)item.Value).Cast<IEnumerable<NuGet.PackageDependencySet>>();
                        if (list.Any())
                        {
                            obj.SetValue(someObject, list, null);
                        }
                    }
                    else if (obj.PropertyType == typeof(ICollection<NuGet.PackageReferenceSet>))
                    {
                        var list = ((IEnumerable<object>)item.Value);
                        foreach (var listItem in list)
                        {
                            //obj.SetValue(someObject, list, null);
                        }
                    }
                    else if (obj.PropertyType == typeof(IEnumerable<NuGet.IPackageAssemblyReference>))
                    {
                        var list = ((IEnumerable<object>)item.Value);
                        foreach (var listItem in list)
                        {
                            //obj.SetValue(someObject, list, null);
                        }
                    }
                    else if (obj.PropertyType == typeof(IEnumerable<string>))
                    {
                        var array = (object[]) item.Value;
                        obj.SetValue(someObject, array.Select(o => o.ToString()), null);
                    }
                    else if (obj.PropertyType == typeof(Uri))
                    {
                        if (item.Value != null)
                        {
                            var uri = item.Value.ToString();
                            obj.SetValue(someObject, new Uri(uri), null);
                        }
                    }
                    else if (obj.PropertyType == typeof(DateTimeOffset?))
                    {
                        if (item.Value != null)
                        {
                            var datetime = item.Value.ToString();
                            obj.SetValue(someObject, DateTimeOffset.Parse(datetime), null);
                        }
                    }
                    else if (obj.PropertyType == typeof(DateTime))
                    {
                        if (item.Value != null)
                        {
                            var datetime = item.Value.ToString();
                            obj.SetValue(someObject, DateTime.Parse(datetime), null);
                        }
                    }
                    else
                    {
                        obj.SetValue(someObject, item.Value, null);
                    }
                }
            }

            return someObject;
        }

        public static IDictionary<string, object> AsDictionary(this object source, BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
        {
            return source.GetType().GetProperties(bindingAttr).ToDictionary
            (
                propInfo => propInfo.Name,
                propInfo => propInfo.GetValue(source, null)
            );

        }

        private static Version GetVersion(IDictionary<string, object> versionDict)
        {

            var major = (int)versionDict["Major"];
            var minor = (int)versionDict["Minor"];
            var buid = (int)versionDict["Build"];
            var revision = (int)versionDict["Revision"];
            if (buid < 0 || revision < 0)
            {
                var version = new Version(major, minor);
                return version;
            }
            else
            {
                var version = new Version(major, minor, buid, revision);
                return version;
            }
        }
    }
}