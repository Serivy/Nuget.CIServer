using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Dapper;
using NuGet.Server.Infrastructure;
using NuGet.Server.Models;

namespace NuGet.Server.DataModel
{
    public class DataStore
    {
        private static DataStore instance;
        public string ConnectionString;

        public DataStore()
        {
            // Setup the database.
            var conn = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
            var connection = new SqlConnectionStringBuilder(conn);

            connection.InitialCatalog = !string.IsNullOrEmpty(connection.InitialCatalog) ? connection.InitialCatalog : "OpenNuget";
            connection.DataSource = connection.DataSource ?? "(localdb)\\v11.0";
            ConnectionString = connection.ToString();

            EnsureDatabaseExists(ConnectionString);

            return;

            //new FileInfo(Assembly.GetEntryAssembly().Location).Directory;
            var targetDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NugetCi", "Cache.mdf");
            var file = new FileInfo(targetDb);

            if (!file.Exists)
            {
                if (!Directory.Exists(file.Directory.FullName))
                {
                    Directory.CreateDirectory(file.Directory.FullName);
                }

                using (var localConn = new SqlConnection("Server=(localdb)\\v11.0;Integrated Security=true;"))
                {
                    localConn.Open();
                    var cmd = localConn.CreateCommand();
                    cmd.CommandText = "DECLARE @databaseName sysname " + Environment.NewLine +
                                      "SET @databaseName = CONVERT(sysname, NEWID())" + Environment.NewLine +
                                      "WHILE EXISTS (SELECT name FROM sys.databases WHERE name = @databaseName)" + Environment.NewLine +
                                      "BEGIN" + Environment.NewLine +
                                      "	SET @databaseName = CONVERT(sysname, NEWID())" + Environment.NewLine +
                                      "END" + Environment.NewLine +
                                      "SET @databaseName = '[' + @databaseName + ']'" + Environment.NewLine +
                                      "DECLARE @sqlString nvarchar(MAX)" + Environment.NewLine +
                                      "SET @sqlString = 'CREATE DATABASE ' + @databaseName + N' ON ( NAME = [Database1], FILENAME = N''" + targetDb + "'')'" + Environment.NewLine +
                                      "EXEC sp_executesql @sqlString" + Environment.NewLine +
                                      "SET @sqlString = 'ALTER DATABASE ' + @databaseName + ' SET AUTO_SHRINK ON'" + Environment.NewLine +
                                      "EXEC sp_executesql @sqlString" + Environment.NewLine +
                                      "SET @sqlString = 'ALTER DATABASE ' + @databaseName + ' SET OFFLINE WITH ROLLBACK IMMEDIATE'" + Environment.NewLine +
                                      "EXEC sp_executesql @sqlString" + Environment.NewLine +
                                      "SET @sqlString = 'EXEC sp_detach_db ' + @databaseName" + Environment.NewLine +
                                      "EXEC sp_executesql @sqlString";

                    cmd.ExecuteNonQuery();
                }
            }

            ConnectionString = @"Server=(localdb)\v11.0;Integrated Security=true;AttachDbFileName=" + targetDb;

            EnsureDatabaseLatest();

        }

        private void EnsureDatabaseExists(string connnectionString)
        {
            try
            {
                using (var conn = new SqlConnection(connnectionString))
                {
                    conn.Open();
                }
            }
            catch (Exception ex)
            {
                // Datastore may not exist, try create it.
                var masterDB = new SqlConnectionStringBuilder(connnectionString);
                var dataStore = masterDB.InitialCatalog;
                masterDB.InitialCatalog = "master";
                using (var conn = new SqlConnection(masterDB.ToString()))
                {
                    conn.Open();
                    conn.Execute(string.Format("CREATE DATABASE {0}", dataStore));
                }
            }

            EnsureDatabaseLatest();
        }

        public static DataStore GetInstance()
        {
            if (instance == null)
            {
                instance = new DataStore();
            }

            return instance;
        }

        public object GetPackage(string filename)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                var package = conn.Query<DerivedPackageData>("Select * from Package where [File] = " + filename).First();
                return package;
            }
        }

        public Dictionary<string, PackageInfo> GetAllPackages()
        {
            var packages = new Dictionary<string, PackageInfo>();
            try
            {
                var jss = new JavaScriptSerializer();
                jss.RegisterConverters(new [] { new PackageJavaScriptConverter() });

                using (var conn = GetConnection())
                {
                    conn.Open();
                    var queried = conn.Query("Select Id, Package, DerivedPackageData from Package");
                    //.Select(o => new Tuple<string, object, object>(o.Id, o.Package, o.DerivedPackageData));
                    //.Select(o => new { Id = o.Id, Package = o.Package, DerivedPackageData = o.DerivedPackageData });

                    foreach (var q in queried)
                    {
                        var info = jss.Deserialize<PackageModel>(q.Package);
                        var additional = jss.Deserialize<DerivedPackageData>(q.DerivedPackageData);

                        ////var dataObj = serializer.Deserialize<DataObject>(json);

                        //var info = (Dictionary<string, object>) jss.DeserializeObject(q.Package);
                        //var pm = info.ToObject<PackageModel>();
                        packages.Add(q.Id, new PackageInfo() { Package = info, DerivedPackageData = additional });
                    }
                }
            }
            catch (Exception)
            {
                RepairDatabase();
            }

            return packages;
        }

        public void AddPackage(string filename, object zip, object data)
        {
            var json = new JavaScriptSerializer();
            json.RegisterConverters(new[] { new PackageJavaScriptSerializer() });
            var zipj = json.Serialize(zip);
            var dataj = json.Serialize(data);
            var fileInfo = new FileInfo(filename);

            using (var conn = GetConnection())
            {
                try
                {
                    conn.Open();
                    conn.Execute("Delete Package Where Id = '" + fileInfo.Name + "'");
                    var query = string.Format("Insert into Package (Id, Package, DerivedPackageData) select '{0}', '{1}', '{2}'", fileInfo.Name, zipj, dataj);
                    conn.Execute(query);
                }
                catch (Exception e)
                {
                    RepairDatabase();
                    throw e;
                }
            }
        }

        public void UpdatePackageHashes(string filename, object zip, object data)
        {
            AddPackage(filename, zip, data);
        }

        public void CleanupPackages(Dictionary<string, PackageInfo> obsoletePackages)
        {
            using (var conn = GetConnection())
            {
                try
                {
                    conn.Open();
                    foreach (var package in obsoletePackages)
                    {
                        conn.Execute("Delete Package Where Id = '" + package.Key + "'");
                    }
                }
                catch (Exception e)
                {
                    RepairDatabase();
                    throw e;
                }
            }
        }

        private void ExecuteCommand(string text, SqlConnection conn = null)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = text;
            cmd.ExecuteNonQuery();
        }

        private SqlConnection GetConnection()
        {
            var conn = new SqlConnection(ConnectionString);
            return conn;
        }

        private void RepairDatabase()
        {
            using (var conn = GetConnection())
            {
                conn.Query("if OBJECT_ID('dbo.Package') is not null begin Drop table dbo.Package end");
            }
            EnsureDatabaseLatest();
        }

        private void EnsureDatabaseLatest()
        {
            using (var conn = GetConnection())
            {
                conn.Open();

                var assembly = Assembly.GetExecutingAssembly();
                var sqlFiles = assembly.GetManifestResourceNames().Where(o => o.EndsWith("Pop.sql"));

                foreach (var resource in sqlFiles)
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resource))
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string result = reader.ReadToEnd();
                            ExecuteCommand(result, conn);
                        }
                    }
                }
            }
        }
    }
}
