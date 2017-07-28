using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Management.Storage.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Cryptography;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;

namespace AzureStackStorage
{
    class Program
    {
        // These values are used by the sample as defaults to create a new storage account. You can specify any location and any storage account type.
        const string DefaultLocation = "local";
        public static Microsoft.Azure.Management.Storage.Models.Sku DefaultSku = new Microsoft.Azure.Management.Storage.Models.Sku(SkuName.StandardLRS);
        public static Kind DefaultKind = Kind.Storage;
        public static Dictionary<string, string> DefaultTags = new Dictionary<string, string>
        {
            {"key1","value1"},
            {"key2","value2"}
        };

        public static void PrintStorageAccountKeys(IReadOnlyList<StorageAccountKey> storageAccountKeys)
        {
            foreach (var storageAccountKey in storageAccountKeys)
            {
                Console.WriteLine($"Key {storageAccountKey.KeyName} = {storageAccountKey.Value}");
            }
        }

        public static void PrintStorageAccount(StorageAccount sa)
        {
            Console.WriteLine($"{sa.Name} created @ {sa.CreationTime}");
        }

        public async static Task<Microsoft.Rest.ServiceClientCredentials> AzureAuthenticateAsync()
        {
            try
            {
                ActiveDirectoryServiceSettings s = new ActiveDirectoryServiceSettings();
                s.AuthenticationEndpoint = new Uri(@"https://login.windows.net/");
                s.TokenAudience = new Uri(@"https://management.hugazurestack.onmicrosoft.com/4c5fa865-6085-48c3-a5b8-155168ecf0c2");
                s.ValidateAuthority = true;
                string seckey = @"xMgY4XIsFwr9zdfJXCGONnfliOQdaq4kMqUyvQ8iL/0=";

                string tid = "12deab2d-829f-4e48-9520-d7135777b9ee";
                string cid = "7932c226-38d4-444c-bae4-a49db302656b";


                var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(tid, cid, seckey, s);

                return serviceCreds;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadLine();
                return null;
            }
        }

        private static string generageRamdonName(string pre, int length)
        {
            var _constent = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var r = new Random();
            string result = pre;
            while (length > 0)
            {
                result += _constent[r.Next(0,_constent.Length)];
                length--;
            }

            return result;
        }

        public async static Task SampleRunAsync()
        {
            string muri = @"https://management.local.azurestack.external/";
            string sid = "bce9b432-73c1-466f-83a7-53840ec5682d";

            var creds = await AzureAuthenticateAsync();

            var resourceClient = new ResourceManagementClient(creds)
            {
                BaseUri = new Uri(muri),
                SubscriptionId = sid
            };

            var storageClient = new StorageManagementClient(creds)
            {
                BaseUri = new Uri(muri),
                SubscriptionId = sid
            };

            StorageSampleE2E(resourceClient, storageClient);
        }

        private static void StorageBlobSample(string accountName, string key)
        {
            StorageCredentials cre = new StorageCredentials(accountName, key);
            CloudStorageAccount storageAccount = new CloudStorageAccount(cre, "local.azurestack.external", true);

            CloudBlobClient blob = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer blobContainer = blob.GetContainerReference("sample");

            blobContainer.CreateIfNotExists();

            CloudBlockBlob bb = blobContainer.GetBlockBlobReference("blockblob1");

            // prepare a random content for upload/download 
            int size = 5 * 1024;
            byte[] buffer = new byte[size];
            Random rand = new Random();
            rand.NextBytes(buffer);
            Console.WriteLine("Prepare the random content to upload:");
            Console.WriteLine(Encoding.Unicode.GetString(buffer));

            Console.WriteLine("Uploading the blob.");
            using (MemoryStream stream = new MemoryStream(buffer))
            {
                bb.UploadFromStream(stream,size);
            }

            Console.WriteLine("Downloading a blob.");

            // Download and decrypt the encrypted contents from the blob.
            using (MemoryStream outputStream = new MemoryStream())
            {
                bb.DownloadToStream(outputStream, null, null);
                StreamReader reader = new StreamReader(outputStream);
                string content = reader.ReadLine();
                Console.WriteLine("The downloaded content:");
                Console.WriteLine(content);
            }

            Console.WriteLine("\nPress enter key to continue ...");
            Console.ReadLine(); 
        }

        private static void StorageSampleE2E(ResourceManagementClient resourceClient, StorageManagementClient storage)
        {
            string rgName = generageRamdonName("rgAzS", 20);
            string storageAccountName1 = generageRamdonName("sa1", 20).ToLower();
            string storageAccountName2 = generageRamdonName("sa2", 20).ToLower();

            try
            {
                //Register the Storage Resource Provider with the Subscription
                RegisterStorageResourceProvider(resourceClient);

                //Create a new resource group
                CreateResourceGroup(rgName, resourceClient);

                //Create a new KeyVault
                string vaultUri = CreateKeyVault(rgName, resourceClient, "KeyVaultSample");

                //Create a new account in a specific resource group with the specified account name                     
                CreateStorageAccount(rgName, storageAccountName1, storage);

                //Get all the account properties for a given resource group and account name
                StorageAccount storAcct = storage.StorageAccounts.GetProperties(rgName, storageAccountName1);

                //Get a list of storage accounts within a specific resource group
                IEnumerable<StorageAccount> storAccts = storage.StorageAccounts.ListByResourceGroup(rgName);

                //Get all the storage accounts for a given subscription
                IEnumerable<StorageAccount> storAcctsSub = storage.StorageAccounts.List();

                //Get the storage account keys for a given account and resource group
                IList<StorageAccountKey> acctKeys = storage.StorageAccounts.ListKeys(rgName, storageAccountName1).Keys;

                StorageBlobSample(storageAccountName1, acctKeys[0].Value);

                //Check if the account name is available
                bool? nameAvailable = storage.StorageAccounts.CheckNameAvailability(storageAccountName1).NameAvailable;

                //Delete a storage account with the given account name and a resource group
                DeleteStorageAccount(rgName, storageAccountName1, storage);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                // clean up the resource groups created in this sample code 

                if (resourceClient.ResourceGroups.CheckExistence(rgName))
                {
                    Console.WriteLine("Deleting the ResourceGroup of " + rgName);
                    resourceClient.ResourceGroups.Delete(rgName);
                    Console.WriteLine("Testing resource group is cleaned up.");
                }
            }
        }


        /// Registers the Storage Resource Provider in the subscription.
        public static void RegisterStorageResourceProvider(ResourceManagementClient resourcesClient)
        {
            Console.WriteLine("Registering Storage Resource Provider with subscription...");
            resourcesClient.Providers.Register("Microsoft.Storage");
            Console.WriteLine("Storage Resource Provider registered.\n");
        }


        /// Creates a new resource group with the specified name
        public static void CreateResourceGroup(string rgname, ResourceManagementClient resourcesClient)
        {
            Console.WriteLine("Creating a resource group...");
            var resourceGroup = resourcesClient.ResourceGroups.CreateOrUpdate(
                    rgname,
                    new ResourceGroup
                    {
                        Location = DefaultLocation
                    });
            Console.WriteLine("Resource group created with name " + resourceGroup.Name);
            Console.WriteLine();

        }

        public static string CreateKeyVault(string rgName, ResourceManagementClient rmClient, string kbName)
        {
            Console.WriteLine("Create a Key Vault resource with a generic PUT");
            var keyVaultParams = new GenericResource
            {
                Location = "local",
                Properties = new Dictionary<string, object>{
                    {"tenantId", "12deab2d-829f-4e48-9520-d7135777b9ee"},
                    {"sku", new Dictionary<string, object>{{"family", "A"}, {"name", "standard"}}},
                    {"accessPolicies", Array.CreateInstance(typeof(string), 0)},
                    {"enabledForDeployment", true},
                    {"enabledForTemplateDeployment", true},
                    {"enabledForDiskEncryption", true}
                }
            };

            var keyVault = rmClient.Resources.CreateOrUpdate(
                rgName,
                "Microsoft.KeyVault",
                "",
                "vaults",
                kbName,
                "2015-06-01", 
                keyVaultParams);

            Console.WriteLine("Key Vault Name: {0} ", keyVault.Name);
            Console.WriteLine("Key Vault Id: {0} ", keyVault.Id);
            JObject joProperties = JObject.Parse(keyVault.Properties.ToString());
            string vaultUri = joProperties["vaultUri"].ToString();
            Console.WriteLine("Key Vault BaseURI: {0} ", vaultUri);
            return vaultUri;
        }

        /// Create a new Storage Account. If one already exists then the request still succeeds
        private static void CreateStorageAccount(string rgname, string acctName, StorageManagementClient storageMgmtClient)
        {
            StorageAccountCreateParameters parameters = GetDefaultStorageAccountParameters();

            Console.WriteLine("Creating a storage account...");
            var storageAccount = storageMgmtClient.StorageAccounts.Create(rgname, acctName, parameters);
            Console.WriteLine("Storage account created with name " + storageAccount.Name);
            Console.WriteLine();
        }

        /// Deletes a storage account for the specified account name
        private static void DeleteStorageAccount(string rgname, string acctName, StorageManagementClient storageMgmtClient)
        {
            Console.WriteLine("Deleting a storage account...");
            storageMgmtClient.StorageAccounts.Delete(rgname, acctName);
            Console.WriteLine("Storage account " + acctName + " deleted");
            Console.WriteLine();
        }


        /// Returns default values to create a storage account
        private static StorageAccountCreateParameters GetDefaultStorageAccountParameters()
        {
            StorageAccountCreateParameters account = new StorageAccountCreateParameters
            {
                Location = DefaultLocation,
                Kind = DefaultKind,
                Tags = DefaultTags,
                Sku = DefaultSku
            };

            return account;
        }
        public static void Main(string[] args)
        {
            try
            {
                SampleRunAsync().Wait();

                Console.WriteLine("Press any key to exit.\n");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadLine();
            }
            
        }
    }
}
