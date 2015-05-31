using System.Text;
using System.Web;
using Ninject;
using NuGet.Resources;
using NuGet.Server.DataServices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Configuration;
using Elmah;
using NuGet.Server.DataModel;
using NuGet.Server.Models;

namespace NuGet.Server.Infrastructure
{
    /// <summary>
    /// ServerPackageRepository represents a folder of nupkgs on disk. All packages are cached during the first request in order
    /// to correctly determine attributes such as IsAbsoluteLatestVersion. Adding, removing, or making changes to packages on disk 
    /// will clear the cache.
    /// </summary>
    public class ServerPackageRepository : PackageRepositoryBase, IServerPackageRepository, IPackageLookup, IDisposable
    {
        private IDictionary<IPackage, DerivedPackageData> _packages;
        private readonly object _lockObj = new object();
        private readonly IFileSystem _fileSystem;
        private readonly IPackagePathResolver _pathResolver;
        private readonly Func<string, bool, bool> _getSetting;
        private FileSystemWatcher _fileWatcher;
        private readonly string _filter = String.Format(CultureInfo.InvariantCulture, "*{0}", Constants.PackageExtension);
        private bool _monitoringFiles = false;

        private TimeSpan databaseQueryTime;
        private TimeSpan packageOpeningTime;
        private TimeSpan hashGeneratingTime;
        private TimeSpan derivedPackageDataTime;

        public ServerPackageRepository(string path)
            : this(new DefaultPackagePathResolver(path), new PhysicalFileSystem(path))
        {

        }

        public ServerPackageRepository(IPackagePathResolver pathResolver, IFileSystem fileSystem, Func<string, bool, bool> getSetting = null)
        {
            if (pathResolver == null)
            {
                throw new ArgumentNullException("pathResolver");
            }

            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            _fileSystem = fileSystem;
            _pathResolver = pathResolver;
            _getSetting = getSetting ?? GetBooleanAppSetting;
        }

        [Inject]
        public IHashProvider HashProvider { get; set; }

        public override IQueryable<IPackage> GetPackages()
        {
            return PackageCache.Keys.AsQueryable<IPackage>();
        }

        public IQueryable<Package> GetPackagesWithDerivedData()
        {
            var cache = PackageCache;
            return cache.Keys.Select(p => new Package(p, cache[p])).AsQueryable();
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            return FindPackage(packageId, version) != null;
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            return FindPackagesById(packageId).Where(p => p.Version.Equals(version)).FirstOrDefault();
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            return GetPackages().Where(p => StringComparer.OrdinalIgnoreCase.Compare(p.Id, packageId) == 0);
        }

        /// <summary>
        /// Gives the Package containing both the IPackage and the derived metadata.
        /// The returned Package will be null if <paramref name="package" /> no longer exists in the cache.
        /// </summary>
        public Package GetMetadataPackage(IPackage package)
        {
            Package metadata = null;

            // The cache may have changed, and the metadata may no longer exist
            DerivedPackageData data = null;
            if (PackageCache.TryGetValue(package, out data))
            {
                metadata = new Package(package, data);
            }

            // Todo: Figure out why sometimes this returns null when the package cant be retrieved (usualy during updates to the drop location).
            return metadata;
        }

        public IQueryable<IPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions, bool includeDelisted)
        {
            var cache = PackageCache;

            var packages = cache.Keys.AsQueryable()
                .Find(searchTerm)
                .FilterByPrerelease(allowPrereleaseVersions);
            if (includeDelisted == false)
            {
                packages = packages.Where(p => p.Listed);
            }

            if (EnableFrameworkFiltering && targetFrameworks.Any())
            {
                // Get the list of framework names
                var frameworkNames = targetFrameworks.Select(frameworkName => VersionUtility.ParseFrameworkName(frameworkName));

                packages = packages.Where(package => frameworkNames.Any(frameworkName => VersionUtility.IsCompatible(frameworkName, cache[package].SupportedFrameworks)));
            }

            return packages.AsQueryable();
        }

        public IQueryable<IPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions)
        {
            return Search(searchTerm, targetFrameworks, allowPrereleaseVersions, false);
        }

        public IEnumerable<IPackage> GetUpdates(IEnumerable<IPackageName> packages, bool includePrerelease, bool includeAllVersions, IEnumerable<FrameworkName> targetFrameworks, IEnumerable<IVersionSpec> versionConstraints)
        {
            return this.GetUpdatesCore(packages, includePrerelease, includeAllVersions, targetFrameworks, versionConstraints);
        }

        public override string Source
        {
            get
            {
                return _fileSystem.Root;
            }
        }

        public override bool SupportsPrereleasePackages
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Add a file to the repository.
        /// </summary>
        public override void AddPackage(IPackage package)
        {
            string fileName = _pathResolver.GetPackageFileName(package);
            if (_fileSystem.FileExists(fileName) && !AllowOverrideExistingPackageOnPush)
            {
                throw new InvalidOperationException(String.Format(NuGetResources.Error_PackageAlreadyExists, package));
            }

            lock (_lockObj)
            {
                using (Stream stream = package.GetStream())
                {
                    _fileSystem.AddFile(fileName, stream);
                }

                InvalidatePackages();
            }
        }

        /// <summary>
        /// Unlist or delete a package
        /// </summary>
        public override void RemovePackage(IPackage package)
        {
            if (package != null)
            {
                string fileName = _pathResolver.GetPackageFileName(package);

                lock (_lockObj)
                {
                    if (EnableDelisting)
                    {
                        var fullPath = _fileSystem.GetFullPath(fileName);

                        if (File.Exists(fullPath))
                        {
                            File.SetAttributes(fullPath, File.GetAttributes(fullPath) | FileAttributes.Hidden);
                            // Delisted files can still be queried, therefore not deleting persisted hashes if present.
                            // Also, no need to flip hidden attribute on these since only the one from the nupkg is queried.
                        }
                        else
                        {
                            Debug.Fail("unable to find file");
                        }
                    }
                    else
                    {
                        _fileSystem.DeleteFile(fileName);
                    }

                    InvalidatePackages();
                }
            }
        }

        /// <summary>
        /// Remove a package from the respository.
        /// </summary>
        public void RemovePackage(string packageId, SemanticVersion version)
        {
            IPackage package = FindPackage(packageId, version);

            RemovePackage(package);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            DetachEvents();
        }

        /// <summary>
        /// *.nupkg files in the root folder
        /// </summary>
        private IEnumerable<string> GetPackageFiles()
        {
            var filter = FolderFilter;
            var projects = ConfigurationManager.AppSettings["projects"];
            Regex regex = null;
            if (!string.IsNullOrEmpty(filter))
            {
                regex = new Regex(filter);
            }

            if (string.IsNullOrEmpty(projects))
            {
                // Check top level directory
                foreach (var path in _fileSystem.GetFiles(String.Empty, _filter, true))
                {
                    if (string.IsNullOrEmpty(filter) || regex != null && regex.IsMatch(path))
                    {
                        yield return path;
                    }
                }
            }
            else
            {
                var projs = projects.Split(',');

                var projectDirectories = Directory.GetDirectories(_fileSystem.Root);
                foreach (var proj in projectDirectories)
                {
                    var directory = new DirectoryInfo(proj);
                    if (projs.Contains(directory.Name))
                    {
                        foreach (var path in Directory.GetFiles(directory.FullName, _filter, SearchOption.AllDirectories))
                        {
                            if (string.IsNullOrEmpty(filter) || regex.IsMatch(path))
                            {
                                yield return path;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Internal package cache containing both the packages and their metadata. 
        /// This data is generated if it does not exist already.
        /// </summary>
        private IDictionary<IPackage, DerivedPackageData> PackageCache
        {
            get
            {
                lock (_lockObj)
                {
                    if (_packages == null)
                    {
                        if (!_monitoringFiles)
                        {
                            // attach events the first time
                            _monitoringFiles = true;
                            AttachEvents();
                        }

                        _packages = CreateCache();

                        //Elmah.ErrorSignal.FromCurrentContext().Raise(); .FromContext(context).Raise(new Exception(message, innerException));
                    }

                    return _packages;
                }
            }
        }

        /// <summary>
        /// Sets the current cache to null so it will be regenerated next time.
        /// </summary>
        public void InvalidatePackages()
        {
            lock (_lockObj)
            {
                _packages = null;
            }
        }

        private PackageInfo GetFileData(string path, HttpContext context, bool enableDelisting, bool checkFrameworks)
        {
            OptimizedZipPackage zip = OpenPackage(path);

            //Debug.Assert(zip != null, "Unable to open " + path);
            if (zip == null)
            {
                return null;
            }
            if (enableDelisting)
            {
                // hidden packages are considered delisted
                zip.Listed = !File.GetAttributes(_fileSystem.GetFullPath(path)).HasFlag(FileAttributes.Hidden);
            }


            var data = new DerivedPackageData
            {
                //PackageSize = packageSize,
                //PackageHash = packageHash,
                LastUpdated = _fileSystem.GetLastModified(path),
                Created = _fileSystem.GetCreated(path),
                Path = path,
                FullPath = _fileSystem.GetFullPath(path),

                // default to false, these will be set later
                IsAbsoluteLatestVersion = false,
                IsLatestVersion = false
            };

            if (checkFrameworks)
            {
                data.SupportedFrameworks = zip.GetSupportedFrameworks();
            }

            DataStore.GetInstance().AddPackage(path, zip, data);

            return new PackageInfo() { Package = zip, DerivedPackageData = data }; 
        }

        /// <summary>
        /// CreateCache loads all packages and determines additional metadata such as the hash, IsAbsoluteLatestVersion, and IsLatestVersion.
        /// </summary>
        private IDictionary<IPackage, DerivedPackageData> CreateCache()
        {
            ConcurrentDictionary<IPackage, DerivedPackageData> packages = new ConcurrentDictionary<IPackage, DerivedPackageData>();

            ParallelOptions opts = new ParallelOptions();
            opts.MaxDegreeOfParallelism = 4;

            var timeStamps = new Dictionary<string, TimeSpan>();
            var timer = new Stopwatch();

            ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>> absoluteLatest = new ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>>();
            ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>> latest = new ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>>();

            // get settings
            bool checkFrameworks = EnableFrameworkFiltering;
            bool enableDelisting = EnableDelisting;
            var ignoreHash = IgnoreHash;
            // we need to save the current context because it's stored in TLS and we're computing hashes on different threads.
            var context = HttpContext.Current;

            // load and cache all packages.
            // Note that we can't pass GetPackageFiles() to Parallel.ForEach() because
            // the file could be added/deleted from _fileSystem, and if this happens,
            // we'll get error "Collection was modified; enumeration operation may not execute."
            // So we have to materialize the IEnumerable into a list first.
            var packageFiles = GetPackageFiles().ToList();
            var discoverFiles = new List<string>();

            Action<Tuple<IPackage, DerivedPackageData>> addToPackage = delegate(Tuple<IPackage, DerivedPackageData> entry)
            {
                var newPackage = entry.Item1;
                // find the latest versions
                string id = newPackage.Id.ToLowerInvariant();

                // update with the highest version
                absoluteLatest.AddOrUpdate(id, entry, (oldId, oldEntry) => oldEntry.Item1.Version < entry.Item1.Version ? entry : oldEntry);

                // update latest for release versions
                if (entry.Item1.IsReleaseVersion())
                {
                    latest.AddOrUpdate(id, entry, (oldId, oldEntry) => oldEntry.Item1.Version < entry.Item1.Version ? entry : oldEntry);
                }

                // add the package to the cache, it should not exist already
                Debug.Assert(packages.ContainsKey(entry.Item1) == false, "duplicate package added");
                packages.AddOrUpdate(entry.Item1, entry.Item2, (oldPkg, oldData) => oldData);
            };

            // Load the caches for files.
            timer.Restart();
            var cachedPackages = DataStore.GetInstance().GetAllPackages();
            foreach (var file in packageFiles)
            {
                PackageInfo entryNew;
                var fileInfo = new FileInfo(file);
                if (cachedPackages.TryGetValue(fileInfo.Name, out entryNew))
                {
                    cachedPackages.Remove(fileInfo.Name);
                    var entry = new Tuple<IPackage, DerivedPackageData>(entryNew.Package, entryNew.DerivedPackageData);
                    addToPackage(entry);
                }
                else
                {
                    discoverFiles.Add(file);
                }
            }
            timer.Stop();
            timeStamps.Add(string.Format("Database parsing for {0} files", cachedPackages.Count()), timer.Elapsed);

            // Open packages for items that are not in the database.
            timer.Restart();
            Parallel.ForEach(discoverFiles, opts, path =>
            {
                var entryNew = GetFileData(path, context, enableDelisting, checkFrameworks);
                if (entryNew == null)
                {
                    // Can be null if the package failed to open if it was in use.
                    return;
                }
                var entry = new Tuple<IPackage, DerivedPackageData>(entryNew.Package, entryNew.DerivedPackageData);
                addToPackage(entry);
            });
            timer.Stop();
            timeStamps.Add(string.Format("Package opening for {0} files", discoverFiles.Count()), timer.Elapsed);

            // Calculate Hashes.

            if (!ignoreHash)
            {
                timer.Restart();
                var hashlessFiles = packages.Where(o => o.Value.PackageSize < 1).ToArray();
                Parallel.ForEach(hashlessFiles, opts, package =>
                {
                    using (var stream = _fileSystem.OpenFile(package.Value.FullPath))
                    {
                        package.Value.PackageSize = stream.Length;
                        package.Value.PackageHash = Convert.ToBase64String(HashProvider.CalculateHash(stream));
                    }
                    DataStore.GetInstance().UpdatePackageHashes(package.Value.FullPath, package.Key, package.Value);
                });
                timer.Stop();
                timeStamps.Add(string.Format("Hash calculations for {0} files", hashlessFiles.Length), timer.Elapsed); 
            }
            
            // Set additional attributes after visiting all packages
            foreach (var entry in absoluteLatest.Values)
            {
                entry.Item2.IsAbsoluteLatestVersion = true;
            }

            foreach (var entry in latest.Values)
            {
                entry.Item2.IsLatestVersion = true;
            }

            //packages.GroupBy(o => o.Key.Id,).Select(o => )

            var message = string.Format("{0}{1}", "Cache Loaded => ", string.Join(", ", timeStamps.Select(o => o.Key + ": " + o.Value.TotalMilliseconds)));
            ErrorSignal.FromCurrentContext().Raise(new Exception(message, new NotSupportedException()));

            DataStore.GetInstance().CleanupPackages(cachedPackages);

            return packages;
        }

        private OptimizedZipPackage OpenPackage(string path)
        {
            OptimizedZipPackage zip = null;

            if (_fileSystem.FileExists(path))
            {
                try
                {
                    zip = new OptimizedZipPackage(_fileSystem, path);
                }
                catch (FileFormatException ex)
                {
                    throw new InvalidDataException(
                        String.Format(CultureInfo.CurrentCulture, NuGetResources.ErrorReadingPackage, path), ex);
                }
                catch (IOException)
                {
                    // Probably because its currently being copied. ignore it this time.
                    return null;
                }
                catch (InvalidOperationException)
                {
                    // May be a broken nuget file. Should probably clean it up here.
                    return null;
                }
                // Set the last modified date on the package
                zip.Published = _fileSystem.GetLastModified(path);
            }

            return zip;
        }

        // Add the file watcher to monitor changes on disk
        private void AttachEvents()
        {
            // skip invalid paths
            if (_fileWatcher == null && !String.IsNullOrEmpty(Source) && Directory.Exists(Source))
            {
                _fileWatcher = new FileSystemWatcher(Source);
                _fileWatcher.Filter = _filter;
                _fileWatcher.IncludeSubdirectories = true;

                _fileWatcher.Changed += FileChanged;
                _fileWatcher.Created += FileChanged;
                _fileWatcher.Deleted += FileChanged;
                _fileWatcher.Renamed += FileChanged;

                _fileWatcher.EnableRaisingEvents = true;
            }
        }

        // clean up events
        private void DetachEvents()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= FileChanged;
                _fileWatcher.Created -= FileChanged;
                _fileWatcher.Deleted -= FileChanged;
                _fileWatcher.Renamed -= FileChanged;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            // invalidate the cache when a nupkg in the root folder changes
            // TODO: invalidating *all* packages for every nupkg change under this folder seems more expensive than it should.
            // Recommend using e.FullPath to figure out which nupkgs need to be (re)computed.
            InvalidatePackages();
        }

        private bool AllowOverrideExistingPackageOnPush
        {
            get
            {
                // If the setting is misconfigured, treat it as success (backwards compatibility).
                return _getSetting("allowOverrideExistingPackageOnPush", true);
            }
        }

        private bool EnableDelisting
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _getSetting("enableDelisting", false);
            }
        }

        private bool EnableFrameworkFiltering
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _getSetting("enableFrameworkFiltering", false);
            }
        }

        private bool IgnoreHash
        {
            get
            {
                return _getSetting("ignoreHash", false);
            }
        }

        private string FolderFilter
        {
            get
            {
                return GetStringAppSetting("folderFilter");
            }
        }

        private static bool GetBooleanAppSetting(string key, bool defaultValue)
        {
            var appSettings = WebConfigurationManager.AppSettings;
            bool value;
            return !Boolean.TryParse(appSettings[key], out value) ? defaultValue : value;
        }

        private static string GetStringAppSetting(string key)
        {
            var appSettings = WebConfigurationManager.AppSettings;
            return appSettings[key];
        }
    }
}