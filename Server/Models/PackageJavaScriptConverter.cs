using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Web.Script.Serialization;

namespace NuGet.Server.Models
{
    public class PackageJavaScriptConverter : JavaScriptConverter
    {
        private static readonly Type[] supported = new[] { 
            typeof(IPackageAssemblyReference), 
            typeof(SemanticVersion), 
            typeof(PackageDependencySet), 
            typeof(IVersionSpec), 
            typeof(PackageDependency) ,
            typeof(FrameworkAssemblyReference),
            typeof(FrameworkName),
            typeof(PackageReferenceSet)
        };

        public override IEnumerable<Type> SupportedTypes
        {
            get { return supported; }
        }

        public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
        {
            if (type == typeof(IPackageAssemblyReference))
            {
                return new PhysicalPackageAssemblyReference()
                {
                    SourcePath = dictionary["SourcePath"].ToString(),
                    TargetPath = dictionary["TargetPath"].ToString()
                };
            }
            else if (type == typeof(SemanticVersion))
            {
                var version = (Version)Deserialize((IDictionary<string, object>)dictionary["Version"], typeof(Version), serializer);
                return new SemanticVersion(version, dictionary["SpecialVersion"].ToString());
            }
            else if (type == typeof(PackageDependencySet))
            {
                FrameworkName targetFramework = null;
                if (dictionary["TargetFramework"] != null)
                {
                    targetFramework = (FrameworkName)Deserialize((IDictionary<string, object>)dictionary["TargetFramework"], typeof(FrameworkName), serializer);
                }

                var packageDependency = new List<PackageDependency>();
                foreach (IDictionary<string, object> dependancy in (ArrayList)dictionary["Dependencies"])
                {
                    var dependancyResolved = (PackageDependency)Deserialize(dependancy, typeof(PackageDependency), serializer);
                    packageDependency.Add(dependancyResolved);
                }

                return new PackageDependencySet(targetFramework, packageDependency);
            }
            else if (type == typeof(PackageDependency))
            {

                var versionSpec = (IVersionSpec)Deserialize((IDictionary<string, object>)dictionary["VersionSpec"], typeof(IVersionSpec), serializer);
                return new PackageDependency(dictionary["Id"].ToString(), versionSpec);
            }
            else if (type == typeof(IVersionSpec))
            {
                var maxVersion = dictionary["MaxVersion"] != null ? (SemanticVersion)Deserialize((IDictionary<string, object>)dictionary["MaxVersion"], typeof(SemanticVersion), serializer) : null;
                var minVersion = dictionary["MinVersion"] != null ? (SemanticVersion)Deserialize((IDictionary<string, object>)dictionary["MinVersion"], typeof(SemanticVersion), serializer) : null;
                return new VersionSpec()
                {
                    MaxVersion = maxVersion,
                    MinVersion = minVersion,
                    IsMaxInclusive = (bool)dictionary["IsMaxInclusive"],
                    IsMinInclusive = (bool)dictionary["IsMinInclusive"]
                };
            }
            else if (type == typeof(Version))
            {
                Version version;
                var major = (int)dictionary["Major"];
                var minor = (int)dictionary["Minor"];
                var buid = (int)dictionary["Build"];
                var revision = (int)dictionary["Revision"];
                if (buid < 0 || revision < 0)
                {
                    version = new Version(major, minor);
                }
                else
                {
                    version = new Version(major, minor, buid, revision);
                }

                return version;
            }
            else if (type == typeof(FrameworkAssemblyReference))
            {
                var supportedFrameworks = new List<FrameworkName>();
                foreach (IDictionary<string, object> framework in (ArrayList)dictionary["SupportedFrameworks"])
                {
                    var frameworkObj = (FrameworkName)Deserialize(framework, typeof(FrameworkName), serializer);
                    supportedFrameworks.Add(frameworkObj);
                }

                return new FrameworkAssemblyReference(dictionary["AssemblyName"].ToString(), supportedFrameworks);
            }
            else if (type == typeof(FrameworkName))
            {
                var version = (Version)Deserialize((IDictionary<string, object>)dictionary["Version"], typeof(Version), serializer);
                var frameworkName = new FrameworkName(dictionary["Identifier"].ToString(), version);
                return frameworkName;
            }
            else if (type == typeof(PackageReferenceSet))
            {
                var referenceSet = new PackageReferenceSet(null, ((ArrayList)dictionary["References"]).Cast<string>());
                return referenceSet;
            }

            return null;
        }

        public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
        {
            var dataObj = obj as PackageModel;
            //if (dataObj != null)
            //{
            //    return new Dictionary<string, object>
            //    {
            //        {"user_id", dataObj.UserId},
            //        {"detail_level", dataObj.DetailLevel}
            //    };
            //}
            return new Dictionary<string, object>();
        }
    }
}