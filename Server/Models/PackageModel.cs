using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Web;

namespace NuGet.Server.Models
{
    public class PackageModel : IPackage
    {
        public string Id { get; set; }

        public SemanticVersion Version { get; set; }

        public string Title { get; set; }

        public IEnumerable<string> Authors { get; set; }

        public IEnumerable<string> Owners { get; set; }

        public Uri IconUrl { get; set; }
        
        public Uri LicenseUrl { get; set; }
        
        public Uri ProjectUrl { get; set; }

        public bool RequireLicenseAcceptance { get; set; }

        public bool DevelopmentDependency { get; set; }

        public string Description { get; set; }

        public string Summary { get; set; }

        public string ReleaseNotes { get; set; }

        public string Language { get; set; }

        public string Tags { get; set; }

        public string Copyright { get; set; }

        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; set; }

        public ICollection<PackageReferenceSet> PackageAssemblyReferences { get; set; }

        public IEnumerable<PackageDependencySet> DependencySets { get; set; }

        public Version MinClientVersion { get; set; }

        public Uri ReportAbuseUrl { get; set; }

        public int DownloadCount { get; set; }

        public bool IsAbsoluteLatestVersion { get; set; }

        public bool IsLatestVersion { get; set; }

        public bool Listed { get; set; }

        public DateTimeOffset? Published { get; set; }

        public IEnumerable<IPackageAssemblyReference> AssemblyReferences { get; set; }

        public IEnumerable<IPackageFile> GetFiles()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<FrameworkName> GetSupportedFrameworks()
        {
            throw new NotImplementedException();
        }

        public Stream GetStream()
        {
            throw new NotImplementedException();
        }
    }
}