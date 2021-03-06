﻿//
// Copyright (c) 2017 Jovan Popovic
// Licence: MIT
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
// 
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using QueryExecutionEngine;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AzureBCP
{
    public class Program
    {
        private static string Server;
        private static string Database;
        private static bool IntegratedSecurity = false;
        private static string Username;
        private static string Password;
        private static string Table;
        private static string Source;
        private static Dictionary<string, string> BulkInsertOptions = new Dictionary<string, string>();
        private static string directory;
        private static string file;
        private static string StorageDataSource = null;
        private static string Account;
        private static string Container;
        private static string Sas;
        private static bool Encrypt = false;

        /// <summary>
        /// If connection is in Config.json specify just parameters
        /// AzBcp Sales.Orders IN customer/*.txt -F 2 -t , -r 0x0a
        /// If connection string is not in Config.json, specify connection inline:
        /// AzBcp Sales.Orders IN customer/*.txt -s .\\SQLEXPRESS -d WWI -T -F 2 -t , -r 0x0a -h "TABLOCK"
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            Console.Out.WriteLine("*****************************************************************************");
            Console.Out.WriteLine("*    Azure BCP Utility                                                      *");
            Console.Out.WriteLine("*    Author: Jovan Popovic (jocapc@gmail.com)                               *");
            Console.Out.WriteLine("*    Site:   http://github.com/jocapc/AzBCP                                 *");
            Console.Out.WriteLine("*    Licence:    MIT                                                        *");
            Console.Out.WriteLine("*                                                                           *");
            Console.Out.WriteLine("*    Report issues on:   http://github.com/jocapc/AzBCP/Issues              *");
            Console.Out.WriteLine("*****************************************************************************");

            var config = InitConfig(args);
            GenerateBulkInsertCommandList(config, Sas, Account, Container, directory, file);

            var driver = new Driver(config);

            driver.WorkloadEnd += (o, e) =>
            {
                Console.WriteLine($"Finished {e.Iteration} iterations in {e.ElapsedTimeInSeconds} seconds");
            };

            driver.Error += (o, e) =>
            {
                Console.Error.WriteLine(e.Query.Text + "\nError: " + e.Exception.Message + e.Exception.StackTrace + e.Query.Text);
            };

            driver.QueryEnd += (d, e) =>
            {
                if (e.Iteration < 0)
                {
                    Console.WriteLine($"Finished startup task:\n {e.Query.Text} in {e.ElapsedTime.TotalMilliseconds} ms.\nRow count {e.RowCount}.");
                }
                if (e.Iteration >= 0 && e.Iteration <= d.NumberOfQueries) { 
                    Console.WriteLine($"Finished {e.Iteration+1} of {d.NumberOfQueries} iterations {Math.Round(100.0 * (e.Iteration+1) / d.NumberOfQueries)}%\nExecuted: {e.Query.Text} in {e.ElapsedTime.TotalMilliseconds} ms.\nRow count {e.RowCount}.");
                }
                if (e.Iteration > d.NumberOfQueries)
                {
                    Console.WriteLine($"Finished cleanup task:\n {e.Query.Text} in {e.ElapsedTime.TotalMilliseconds} ms.\nRow count {e.RowCount}.");
                }
            };

            driver.Run();
        }
        

        private static Configuration InitConfig(string[] args)
        {
            Configuration config = null;
            bool wideDataType = false;
            if (File.Exists(System.Configuration.ConfigurationManager.AppSettings["ConfigurationFile"]))
            {
                config = Configuration.LoadFromFile(System.Configuration.ConfigurationManager.AppSettings["ConfigurationFile"]);
            }
            else
            {
                config = new Configuration()
                {
                    WorkerThreads = 1,
                    FailedQueriesLog = "failed-queries.json"
                };
            }

            if (args.Length == 0)
            {
                Exit("You must specify a table name in command.");
            }
            Table = args[0];
            if (args.Length < 2)
            {
                Exit("You must specify source file(s) name in command.");
            }
            var PARAM_POSITION = 3;
            if (args[1] == "IN")
            {
                Source = args[2];
            }
            else
            {
                Source = args[1];
                PARAM_POSITION = 2;
            }
            var segments = Source.Split('/');
            if (segments.Length == 2)
            {
                directory = segments[0];
                file = segments[1];
            }
            else
            {
                file = Source;
            }

            for (int i = PARAM_POSITION; i < args.Length; i++)
            {
                switch (args[i])
                {
                    // Connection options
                    case "-s":
                    case "-server":
                    case "-SERVER":
                        Server = args[++i];
                        break;
                    case "-d":
                    case "-db":
                    case "-DB":
                    case "-database":
                    case "-DATABASE":
                        Database = args[++i];
                        break;
                    case "-T":
                        IntegratedSecurity = true;
                        break;
                    case "-Encrypt":
                    case "-ENCRYPT":
                        Encrypt = true;
                        break;
                    case "-U":
                    case "-User":
                    case "-USER":
                    case "-Username":
                    case "-USERNAME":
                        Username = args[++i];
                        break;
                    case "-P":
                    case "-Pwd":
                    case "-PWD":
                    case "-Password":
                    case "-PASSWORD":
                        Password = args[++i];
                        break;
                    // Azure Storage Options
                    case "-Account":
                    case "-ACCOUNT":
                        Account = args[++i];
                        config.Account = Account;
                        break;
                    case "-Container":
                    case "-CONTAINER":
                        Container = args[++i];
                        config.Container = Container;
                        break;
                    case "-Sas":
                    case "-SAS":
                        Sas = args[++i];
                        config.Sas = Sas;
                        break;
                    case "-DataSource":
                    case "-DATASOURCE":
                        StorageDataSource = args[++i];
                        break;
                    // Bulk Import Options
                    case "-b":
                    case "-batchsize":
                    case "-BatchSize":
                    case "-BATCHSIZE":
                        BulkInsertOptions["BATCHSIZE"] = args[++i];
                        break;
                    case "-F":
                    case "-FirstRow":
                    case "-FIRSTROW":
                        BulkInsertOptions["FIRSTROW"] = args[++i];
                        break;
                    case "-r":
                    case "-rowterminator":
                    case "-RowTerminator":
                    case "-ROWTERMINATOR":
                        BulkInsertOptions["ROWTERMINATOR"] = args[++i];
                        break;
                    case "-t":
                    case "-FieldTerminator":
                    case "-FIELDTERMINATOR":
                        BulkInsertOptions["FIELDTERMINATOR"] = args[++i];
                        break;
                    case "-csv":
                    case "-CSV":
                        BulkInsertOptions["FORMAT"] = "CSV";
                        break;
                    case "-fieldquote":
                    case "-FieldQuote":
                    case "-FIELDQUOTE":
                        BulkInsertOptions["FIELDQUOTE"] = args[++i];
                        break;
                    case "-f":
                    case "-FormatFile":
                    case "-FORMATFILE":
                        BulkInsertOptions["FORMATFILE"] = args[++i];
                        break;
                    case "-C":
                    case "-CodePage":
                    case "-CODEPAGE":
                        BulkInsertOptions["CODEPAGE"] = args[++i];
                        break;
                    case "-c":
                    case "-char":
                    case "-Char":
                    case "-CHAR":
                        BulkInsertOptions["DATAFILETYPE"] = "char";
                        break;
                    case "-n":
                    case "-native":
                    case "-Native":
                    case "-NATIVE":
                        BulkInsertOptions["DATAFILETYPE"] = "native";
                        break;
                    case "-N":
                    case "-widenative":
                    case "-WideNative":
                    case "-WIDENATIVE":
                        BulkInsertOptions["DATAFILETYPE"] = "widenative";
                        break;
                    case "-w":
                    case "-widechar":
                    case "-WideChar":
                    case "-WIDECHAR":
                        BulkInsertOptions["DATAFILETYPE"] = "widechar";
                        break;
                    case "-e":
                    case "-errorfile":
                    case "-ErrorFile":
                    case "-ERRORFILE":
                        BulkInsertOptions["ERRORFILE"] = args[++i];
                        break;
                    case "-m":
                    case "-maxerrors":
                    case "-MaxErrors":
                    case "-MAXERRORS":
                        BulkInsertOptions["MAXERRORS"] = args[++i];
                        break;
                    case "-h":
                    case "-hint":
                    case "-hints":
                    case "-Hint":
                    case "-Hints":
                    case "-HINT":
                    case "-HINTS":
                        var hintList = args[++i].Trim('"');
                        foreach (var hint in hintList.Split(','))
                        {
                            if (hint == "TABLOCK")
                            {
                                BulkInsertOptions["TABLOCK"] = "";
                            }
                            if (hint.StartsWith("ROWS_PER_BATCH"))
                            {
                                var hp = hint.Split('=');
                                if (hp.Length < 2)
                                    Exit("Please specify the value in ROWS_PER_BATCH hint, e.g. ROWS_PER_BATCH=<value>");
                                else
                                    BulkInsertOptions["ROWS_PER_BATCH"] = hp[1].Trim();
                            }
                            if (hint.StartsWith("KILOBYTES_PER_BATCH"))
                            {
                                var hp = hint.Split('=');
                                if (hp.Length < 2)
                                    Exit("Please specify the value in KILOBYTES_PER_BATCH hint, e.g. KILOBYTES_PER_BATCH=<value>");
                                else
                                    BulkInsertOptions["KILOBYTES_PER_BATCH"] = hp[1].Trim();
                            }
                        }
                        break;
                    case "-WorkerThreads":
                    case "-WORKERTHREADS":
                    case "-DOP":
                        config.WorkerThreads = Convert.ToInt16(args[++i]);
                        break;
                    case "-LogOptions":
                        var logOptions = args[++i].Split(',');
                        break;
                    default:
                        break;
                }
            }

            if (Server != null)
            {
                var b = new SqlConnectionStringBuilder()
                {
                    DataSource = Server,
                    InitialCatalog = Database,
                    UserID = Username,
                    Password = Password,
                    IntegratedSecurity = IntegratedSecurity,
                    Encrypt = Encrypt
                };
                config.ConnectionString = b.ConnectionString;
            } else
            {
                if (string.IsNullOrEmpty(config.ConnectionString))
                {
                    Exit("Sql Server connection is not specified in -SERVER command-line option or \"ConnectionString\" property in Config.json file.");
                }
                var b = new SqlConnectionStringBuilder(config.ConnectionString);
                Server = b.DataSource;
                Database = b.InitialCatalog;
            }
            
            Account = Account ?? config.Account;
            Container = Container ?? config.Container;
            Sas = Sas ?? config.Sas;
            StorageDataSource = StorageDataSource ?? config.DataSource;

            return config;
        }

        private static void Exit(string msg)
        {
            Console.WriteLine(msg);
            Console.WriteLine("Usage:");
            Console.WriteLine("azbcp <TABLENAME> IN <FILES> <options>");
            Environment.Exit(-1);
        }

        private static void GenerateBulkInsertCommandList(Configuration config, string sasToken, string accountName, string container, string directory, string pattern)
        {
            string credentialName = null;
            if (StorageDataSource == null)
            {
                ValidateAzureStorageAccountParameters(ref sasToken, ref accountName, container);
                credentialName = "BULK-LOAD-" + accountName + "-" + DateTime.Now.ToShortDateString();
                ConfigureTempDataSource(config, sasToken, accountName, credentialName);
            }
            else
            {
                credentialName = StorageDataSource;
            }

            string[] list = null;
            if (!string.IsNullOrEmpty(sasToken))
            {
                pattern = pattern.Replace(".", "\\.").Replace("*", "\\w+");

                var sc = new StorageCredentials(sasToken);
                CloudStorageAccount storageAccount = new CloudStorageAccount(sc, accountName, "core.windows.net", true);

                var blobClient = storageAccount.CreateCloudBlobClient();
                var srcContainer = blobClient.GetContainerReference(container);

                list = srcContainer
                    .ListBlobs(useFlatBlobListing: true, prefix: directory, blobListingDetails: BlobListingDetails.None)
                    .Where(blob => (blob is CloudBlockBlob) && Regex.IsMatch((blob as CloudBlockBlob).Name, pattern))
                    .Select(b => b.StorageUri.PrimaryUri.PathAndQuery.Trim("/".ToCharArray())).ToArray();
            } else
            {
                if (string.IsNullOrWhiteSpace(StorageDataSource))
                {
                    Exit("You need to specify either a Azure Blob Storage connection using -ACCOUNT, -SAS, and -CONTAINER command-line parameters or EXTERNAL DATA SOURCE name as -DATASOURCE command-line option.");
                }
                if (pattern.Contains('*'))
                {
                    Exit("SAS token is not provided so you cannot specify pattern for input files. Please remove * from input file specification, and put a list of files with comma(,).");
                }
                list = pattern.Split(',');
            }

            if(list.Length < config.WorkerThreads)
            {
                Console.WriteLine("Number of worker threads decreased from {0} to {1}", config.WorkerThreads, list.Length);
                config.WorkerThreads = (short)list.Length;
            }

            Console.WriteLine("Loading {0} files from {1} into {2} database on server {3}.\nPress [Y] to continue:", list.Length, !string.IsNullOrEmpty(sasToken) ? ("Storage account:" + accountName) : ("Storage datasource "+ StorageDataSource), Database, Server);
            var confirmation = Console.ReadLine();
            if (confirmation.ToUpper() == "Y")
            {
                GenerateBulkInsertCommand(config, list, credentialName);
            } else
            {
                Exit("Cancelling operation.");
            }
        }

        private static void ConfigureTempDataSource(Configuration config, string sasToken, string accountName, string credentialName)
        {
            config.Startup = new Query()
            {
                Text = $@"
BEGIN TRY
DROP EXTERNAL DATA SOURCE [{credentialName}] 
DROP DATABASE SCOPED CREDENTIAL [{credentialName}]
END TRY
BEGIN CATCH
	print @@ERROR
END CATCH

CREATE DATABASE SCOPED CREDENTIAL [{credentialName}] 
    WITH IDENTITY = 'SHARED ACCESS SIGNATURE',
    SECRET = '{sasToken}';

CREATE EXTERNAL DATA SOURCE [{credentialName}]
    WITH (	TYPE = BLOB_STORAGE, 
		    LOCATION = 'https://{accountName}.blob.core.windows.net', 
		    CREDENTIAL=  [{credentialName}]);"
            };

            config.Cleanup = new Query()
            {
                Text = $@"
BEGIN TRY
    DROP EXTERNAL DATA SOURCE[{ credentialName }] 
    DROP DATABASE SCOPED CREDENTIAL[{ credentialName }]
END TRY
BEGIN CATCH
    print @@ERROR
END CATCH"
            };
        }

        private static void ValidateAzureStorageAccountParameters(ref string sasToken, ref string accountName, string container)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                Exit("Please specify Azure Blob Storage Account name in -ACCOUNT command-line option or Account property in Config.json file.");
            }
            if (string.IsNullOrWhiteSpace(sasToken))
            {
                Exit("Please specify Azure Blob Storage Shared Account Signature name in -SAS command-line option or Sas property in Config.json file.");
            }
            if (string.IsNullOrWhiteSpace(container))
            {
                Exit("Please specify Azure Blob Storage container name in -CONTAINER command-line option or Container property in Config.json file.");
            }

            if (accountName.EndsWith(".blob.core.windows.net"))
            {
                accountName = accountName.Replace(".blob.core.windows.net", "");
            }
            if (accountName.StartsWith("http://"))
            {
                accountName = accountName.Replace("http://", "");
            }
            if (accountName.StartsWith("https://"))
            {
                accountName = accountName.Replace("https://", "");
            }
            if (sasToken.StartsWith("?"))
            {
                sasToken = sasToken.Substring(1);
            }
        }

        private static void GenerateBulkInsertCommand(Configuration config, string[] list, string externalDataSourceName)
        {
            List<Query> queries = new List<Query>();
            foreach (var file in list)
            {
                string command = $@"BULK INSERT {Table} FROM '{file}' WITH ( DATA_SOURCE = '{externalDataSourceName}',";
                foreach (var option in BulkInsertOptions)
                {
                    command += option.Key;
                    if (option.Value == "")
                        continue;
                    command += " = ";
                    
                    if ((new string[] { "BATCHSIZE", "FIRSTROW", "KILOBYTES_PER_BATCH", "LASTROW", "MAXERRORS", "ROWS_PER_BATCH" }).Any(s => option.Key == s))
                    {
                        command += option.Value;
                    }
                    else
                    {
                        command += "'" + option.Value.Replace("'","''") + "'";
                    }

                    command += ",";
                    if (option.Key == "ERRORFILE")
                    {
                        command += $"ERRORFILE_DATA_SOURCE='{externalDataSourceName}',";
                    }
                    if (option.Key == "FORMATFILE")
                    {
                        command += $"FORMATFILE_DATA_SOURCE='{externalDataSourceName}',";
                    }
                }
                command = command.TrimEnd(',');
                command += ")";

                queries.Add(new Query() { Text = command, IsReader = false });
            }
            config.Queries = queries.ToArray();
        }
    }

    public class DataSource
    {
        public string AccountName;
        public string SAS;
        public string Container;
    }
}
