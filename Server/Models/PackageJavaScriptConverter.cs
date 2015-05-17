using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace NuGet.Server.Models
{
    public class PackageJavaScriptConverter : JavaScriptConverter
    {
        private static readonly Type[] supported = new[] { typeof(IPackageAssemblyReference), typeof(SemanticVersion) };

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