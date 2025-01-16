using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using JabbR.Services;
using Ninject;

namespace JabbR.UploadHandlers
{
    public class AzureBlobStorageHandler : IUploadHandler
    {
        private readonly Func<ApplicationSettings> _settingsFunc;

        private const string JabbRUploadContainer = "jabbr-uploads";

        [ImportingConstructor]
        public AzureBlobStorageHandler(IKernel kernel)
        {
            _settingsFunc = () => kernel.Get<ApplicationSettings>();
        }

        public AzureBlobStorageHandler(ApplicationSettings settings)
        {
            _settingsFunc = () => settings;
        }

        public bool IsValid(string fileName, string contentType)
        {
            // Blob storage can handle any content
            return !string.IsNullOrEmpty(_settingsFunc().AzureblobStorageConnectionString);
        }

        public async Task<UploadResult> UploadFile(string fileName, string contentType, Stream stream)
        {
            var blobServiceClient = new BlobServiceClient(_settingsFunc().AzureblobStorageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(JabbRUploadContainer);

            await containerClient.CreateIfNotExistsAsync();
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            // Randomize the filename everytime so we don't overwrite files
            string randomFile = Path.GetFileNameWithoutExtension(fileName) +
                                "_" +
                                Guid.NewGuid().ToString().Substring(0, 4) + Path.GetExtension(fileName);

            var blobClient = containerClient.GetBlobClient(randomFile);

            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

            var result = new UploadResult
            {
                Url = blobClient.Uri.ToString(),
                Identifier = randomFile
            };

            return result;
        }
    }
}