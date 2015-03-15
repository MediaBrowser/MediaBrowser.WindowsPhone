using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Cimbalino.Toolkit.Helpers;
using Cimbalino.Toolkit.Services;
using MediaBrowser.ApiInteraction.Data;
using MediaBrowser.ApiInteraction.Sync;
using MediaBrowser.Model.ApiClient;
using MediaBrowser.Model.Sync;
using MediaBrowser.WindowsPhone.Extensions;
using Microsoft.Phone.BackgroundTransfer;
using Microsoft.Phone.SecureElement;
using Newtonsoft.Json;
using ScottIsAFool.WindowsPhone.Extensions;

namespace MediaBrowser.WindowsPhone.Model.Sync
{
    public class FileTransferManager : IFileTransferManager
    {
        private const string DownloadLocation = "Shared\\transfers\\sync\\{0}";
        private const string MoveToLocation = "AnyTime\\{0}";
        private readonly ILocalAssetManager _localAssetManager;
        private readonly IStorageServiceHandler _storageService;

        public FileTransferManager(ILocalAssetManager localAssetManager, IStorageService storageService)
        {
            _localAssetManager = localAssetManager;
            _storageService = storageService.Local;
        }

        public async Task GetItemFileAsync(
            IApiClient apiClient, 
            ServerInfo server, 
            LocalItem item, 
            string syncJobItemId, 
            IProgress<double> transferProgress, 
            CancellationToken cancellationToken)
        {
            var downloadUrl = apiClient.GetSyncJobItemFileUrl(syncJobItemId);

            await CreateDownload(downloadUrl, apiClient, item, syncJobItemId);
        }

        private async Task CreateDownload(string source, IApiClient client, LocalItem destiantionFile, string syncJobId)
        {
            var existingRequest = BackgroundTransferService.Requests.FirstOrDefault(x => x.Tag != null && x.Tag.Contains(destiantionFile.Id));
            if (existingRequest != null)
            {
                return;
            }

            await _storageService.CreateDirectoryIfNotThere("AnyTime");
            await _storageService.CreateDirectoryIfNotThere("Shared\\Transfers");
            await _storageService.CreateDirectoryIfNotThere("Shared\\Transfers\\Sync");
            var stringVersion = ApplicationManifest.Current.App.Version;

            var downloader = new BackgroundTransferRequest(new Uri(source, UriKind.Absolute));
            downloader.Headers.Add("X-MediaBrowser-Token", client.AccessToken);
            var authorization = string.Format("MediaBrowser UserId=\"{0}\", Client=\"{1}\", Device=\"{2}\", DeviceId=\"{3}\", Version=\"{4}\"", client.CurrentUserId, client.ClientName, client.DeviceName, client.DeviceId, stringVersion);
            downloader.Headers.Add("Authorization", authorization);
            downloader.Method = "GET";
            downloader.Tag = JsonConvert.SerializeObject(new JobData(destiantionFile.Id, destiantionFile.LocalPath));

            var downloadLocation = new Uri(string.Format(DownloadLocation, destiantionFile.Id), UriKind.RelativeOrAbsolute);
            downloader.DownloadLocation = downloadLocation;
            downloader.TransferStatusChanged += DownloaderOnTransferStatusChanged;

            if (BackgroundTransferService.Requests.Count() == 25)
            {
                // TODO: error or something
                var i = 1;
            }

            var complete = BackgroundTransferService.Requests.Where(x => x.TransferStatus == TransferStatus.Completed).ToList();
            if (!complete.IsNullOrEmpty())
            {
                foreach (var request in complete)
                {
                    BackgroundTransferService.Remove(request);
                }
            }

            BackgroundTransferService.Add(downloader);
        }

        private async void DownloaderOnTransferStatusChanged(object sender, BackgroundTransferEventArgs e)
        {
            if (e.Request.TransferStatus == TransferStatus.Completed)
            {
                var item = JsonConvert.DeserializeObject<JobData>(e.Request.Tag);
                var finalFile = string.Format(MoveToLocation, item.Location);
                var downloadLocation = string.Format(DownloadLocation, item.Id);

                await _storageService.MoveFileIfExists(downloadLocation, finalFile, true);
                await _storageService.DeleteFileIfExists(downloadLocation);
            }
        }

        private class JobData
        {
            public string Id { get; private set; }
            public string Location { get; private set; }

            public JobData(string id, string location)
            {
                Id = id;
                Location = location;
            }
        }
    }
}