using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace NuGet.Server.Models
{
    public class PackageJavaScriptSerializer : JavaScriptConverter
    {
        private static readonly Type[] Supported = new[] { 
            typeof(SemanticVersion), 
        };

        public override IEnumerable<Type> SupportedTypes
        {
            get { return Supported; }
        }

        public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
        {
            return null;
        }

        public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
        {
            if (obj is SemanticVersion)
            {
                return new Dictionary<string, object>
                {
                    {"SemanticVersion", obj.ToString()}
                };
            }

            return null;
        }
    }
}