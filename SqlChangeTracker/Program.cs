﻿using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Topshelf;

namespace SqlChangeTracker
{   
    public class Program
    {
        private static readonly string _name = Assembly.GetExecutingAssembly().GetName().Name;
        public static void Main()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            HostFactory.Run(x =>                                 
            {
                x.Service<Tracker>((sc) =>
                {
                    sc.ConstructUsing(n => new Tracker(OnChange, cts.Token));
                    sc.WhenStarted(tc => { });
                    sc.WhenStopped(tc => { cts.Cancel(); });
                });

                x.RunAsNetworkService();
                
                x.SetDescription(_name);
                x.SetDisplayName(_name);
                x.SetServiceName(_name);
            });            
        }

        public static void OnChange(TrackedRow t, List<RowChange> rowChanges)
        {
            if (!rowChanges.Any()) return;

            var jss = new JsonSerializerSettings() { ContractResolver = new CamelCasePropertyNamesContractResolver(), Formatting = Formatting.Indented };
            var json = JsonConvert.SerializeObject(rowChanges, jss);
            var firstChangeVersion = rowChanges.First().SYS_CHANGE_VERSION;
            var lastChangeVersion = rowChanges.Last().SYS_CHANGE_VERSION;
            string changeFileName = string.Format("{0}-changes-{1}-{2}.json", t.GetFileName(), firstChangeVersion, lastChangeVersion);

            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["WriteChangesToDisk"])) {
                System.IO.File.WriteAllText(changeFileName, json);
            }
            
            string accountName = ConfigurationManager.AppSettings["AccountName"];
            string accountKey = ConfigurationManager.AppSettings["AccountKey"];

            if (!string.IsNullOrEmpty(accountName) && !string.IsNullOrEmpty(accountKey))
            {
                StorageCredentials creds = new StorageCredentials(accountName, accountKey);
                CloudStorageAccount account = new CloudStorageAccount(creds, useHttps: true);

                CloudBlobClient client = account.CreateCloudBlobClient();

                CloudBlobContainer sampleContainer = client.GetContainerReference(_name.ToLowerInvariant());
                sampleContainer.CreateIfNotExists();

                CloudBlockBlob blob = sampleContainer.GetBlockBlobReference(changeFileName);

                blob.UploadText(json);

                blob.Metadata.Add("MachineName", Environment.MachineName);
                blob.Properties.ContentType = "application/json";

                Trace.WriteLine("Uploaded file to Azure");
            }
            else
            {
                Trace.WriteLine("Skipping Azure blob upload, since AccountName and/or AccountKey were not set in configuration.");
            }
        }
    }
}
