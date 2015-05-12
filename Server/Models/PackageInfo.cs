using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.Models
{
    public class PackageInfo
    {
        public IPackage Package { get; set; }

        public DerivedPackageData DerivedPackageData { get; set; }
    }
}