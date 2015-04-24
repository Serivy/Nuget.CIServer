using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.DataModel
{
    public class DataStore
    {
        public static DataStore instance;
        public string ConnectionString;

        public DataStore()
        {
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

        public object GetPackage(string filename)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                var package = conn.Query<DerivedPackageData>("Select * from Package where [File] = " + filename).First();
                return package;
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
