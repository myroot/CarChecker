using CarChecker.Shared;
using LiteDB;
using Microsoft.MobileBlazorBindings.ProtectedStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CarChecker.Data
{
    /// <summary>
    /// App implementation of a vehicle store.
    /// </summary>
    internal sealed class AppVehiclesStore : ILocalVehiclesStore, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly IProtectedStorage protectedStorage;
        private LiteDatabase liteDatabase;
        private Task initTask;

        private ILiteCollection<Vehicle> vehicles;
        private ILiteCollection<Vehicle> localEdits;

        static Random Random = new Random();

        public AppVehiclesStore(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            //this.protectedStorage = protectedStorage ?? throw new ArgumentNullException(nameof(protectedStorage));
        }

        public async ValueTask<string[]> Autocomplete(string prefix)
        {
            await EnsureLiteDb();

            return await Task.Run(() => this.vehicles
                .Query()
                .Where(x => x.LicenseNumber.StartsWith(prefix))
                .OrderBy(x => x.LicenseNumber)
                .Select(x => x.LicenseNumber)
                .Limit(5)
                .ToArray());
        }

        public async ValueTask<DateTime?> GetLastUpdateDateAsync()
        {
            //return await this.protectedStorage.GetAsync<DateTime?>("last_update_date");
            return null;
        }

        public async ValueTask<Vehicle[]> GetOutstandingLocalEditsAsync()
        {
            await EnsureLiteDb();
            return await Task.Run(() => this.localEdits.Query().ToArray());
        }

        public async Task<Vehicle> GetVehicle(string licenseNumber)
        {
            await EnsureLiteDb();

            return await Task.Run(() =>
            {
                var result = this.localEdits.Query().Where(x => x.LicenseNumber.Equals(licenseNumber, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                
                if (result != null)
                {
                    return result;
                }

                return this.vehicles.Query().Where(x => x.LicenseNumber.Equals(licenseNumber, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            });
        }

        public async Task<ClaimsPrincipal> LoadUserAccountAsync()
        {
            var bytes = await this.protectedStorage?.GetAsync<byte[]>("claims_principal");

            if (bytes != null)
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    using var reader = new BinaryReader(stream);
                    return new ClaimsPrincipal(reader);
                }
                catch
                { 
                    // we don't want to fail on trying to restore a claimsprincipal.
                }
            }

            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        public async ValueTask SaveUserAccountAsync(ClaimsPrincipal user)
        {
            if (user == null)
            {
                await this.protectedStorage?.DeleteAsync("claims_principal");
            } else
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                user.WriteTo(writer);
                await this.protectedStorage?.SetAsync("claims_principal", stream.ToArray());
            }
        }

        public async ValueTask SaveVehicleAsync(Vehicle vehicle)
        {
            await EnsureLiteDb();
            await Task.Run(() => this.localEdits.Upsert(vehicle.LicenseNumber, vehicle));
        }

        public async Task SynchronizeAsync()
        {
            await EnsureLiteDb();
            /*
            await Task.Run<Task>(async () =>
            {
                // If there are local edits, always send them first
                foreach (var editedVehicle in this.localEdits.Query().ToArray())
                {
                    (await httpClient.PutAsJsonAsync("api/vehicle/details", editedVehicle)).EnsureSuccessStatusCode();
                    this.localEdits.Delete(editedVehicle.LicenseNumber);
                }
            }).Unwrap();
            */
            await FetchChangesAsync();
        }

        private async Task FetchChangesAsync()
        {
            await EnsureLiteDb();
            var syncDate = DateTime.Now;
            var mostRecentlyUpdated = this.vehicles.Query().OrderByDescending(x => x.LastUpdated).FirstOrDefault();
            var since = mostRecentlyUpdated?.LastUpdated ?? DateTime.MinValue;

            // trick to leave timezone info behind.
            since = new DateTime(since.Ticks, DateTimeKind.Unspecified);
            //var vehicles = await httpClient.GetFromJsonAsync<Vehicle[]>($"api/vehicle/changedvehicles?since={since:o}");
            var vehicles = CreateSeedData();
            foreach (var vehicle in vehicles)
            {
                this.vehicles.Upsert(vehicle.LicenseNumber, vehicle);
            }
            //await this.protectedStorage?.SetAsync("last_update_date", syncDate);
        }

        private static IEnumerable<Vehicle> CreateSeedData()
        {
            var makes = new[] { "Toyota", "Honda", "Mercedes", "Tesla", "BMW", "Kia", "Opel", "Mitsubishi", "Subaru", "Mazda", "Skoda", "Volkswagen", "Audi", "Chrysler", "Daewoo", "Peugeot", "Renault", "Seat", "Volvo", "Land Rover", "Porsche" };
            var models = new[] { "Sprint", "Fury", "Explorer", "Discovery", "305", "920", "Brightside", "XS", "Traveller", "Wanderer", "Pace", "Espresso", "Expert", "Jupiter", "Neptune", "Prowler" };

            for (var i = 0; i < 100; i++)
            {
                yield return new Vehicle
                {
                    LicenseNumber = GenerateRandomLicenseNumber(),
                    Make = PickRandom(makes),
                    Model = PickRandom(models),
                    RegistrationDate = new DateTime(PickRandomRange(2016, 2021), PickRandomRange(1, 13), PickRandomRange(1, 29)),
                    LastUpdated = DateTime.Now,
                    Mileage = PickRandomRange(500, 50000),
                    Tank = PickRandomEnum<FuelLevel>(),
                    Notes = Enumerable.Range(0, PickRandomRange(0, 5)).Select(_ => new InspectionNote
                    {
                        Location = PickRandomEnum<VehiclePart>(),
                        Text = GenerateRandomNoteText()
                    }).ToList()
                };
            }
        }


        static string[] Adjectives = new[] { "Light", "Heavy", "Deep", "Long", "Short", "Substantial", "Slight", "Severe", "Problematic" };
        static string[] Damages = new[] { "Scratch", "Dent", "Ding", "Break", "Discoloration" };
        static string[] Relations = new[] { "towards", "behind", "near", "beside", "along" };
        static string[] Positions = new[] { "Edge", "Side", "Top", "Back", "Front", "Inside", "Outside" };

        private static string GenerateRandomNoteText()
        {
            return PickRandom(new[]
            {
                $"{PickRandom(Adjectives)} {PickRandom(Damages).ToLower()}",
                $"{PickRandom(Adjectives)} {PickRandom(Damages).ToLower()} {PickRandom(Relations)} {PickRandom(Positions).ToLower()}",
                $"{PickRandom(Positions)} has {PickRandom(Damages).ToLower()}",
                $"{PickRandom(Positions)} has {PickRandom(Adjectives).ToLower()} {PickRandom(Damages).ToLower()}",
            });
        }

        private static int PickRandomRange(int minInc, int maxExc)
        {
            return Random.Next(minInc, maxExc);
        }

        private static T PickRandom<T>(T[] values)
        {
            return values[Random.Next(values.Length)];
        }

        public static T PickRandomEnum<T>()
        {
            return PickRandom((T[])Enum.GetValues(typeof(T)));
        }

        private static string GenerateRandomLicenseNumber()
        {
            var result = new StringBuilder();
            result.Append(Random.Next(10));
            result.Append(Random.Next(10));
            result.Append(Random.Next(10));
            result.Append("-");
            result.Append((char)Random.Next('A', 'Z' + 1));
            result.Append((char)Random.Next('A', 'Z' + 1));
            result.Append((char)Random.Next('A', 'Z' + 1));
            return result.ToString();
        }


        private async Task EnsureLiteDb()
        {
            if (liteDatabase != null)
            {
                return;
            }

            void InitTask()
            {
                var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Assembly.GetExecutingAssembly().GetName().Name);
                
                if (!Directory.Exists(dbFolder))
                {
                    Directory.CreateDirectory(dbFolder);
                }

                var dbLocation = Path.Combine(dbFolder, "lite.db");
                liteDatabase = new LiteDatabase(dbLocation);

                vehicles = liteDatabase.GetCollection<Vehicle>("vehicles");

                vehicles.EnsureIndex(x => x.LicenseNumber);
                vehicles.EnsureIndex(x => x.LastUpdated);

                localEdits = liteDatabase.GetCollection<Vehicle>("localEdits");

                localEdits.EnsureIndex(x => x.LicenseNumber);
                localEdits.EnsureIndex(x => x.LastUpdated);
            }

            Task task = null;

            if ((task = Interlocked.CompareExchange(ref initTask, new Task(InitTask), null)) == null)
            {
                task = initTask;
                task.Start(TaskScheduler.Default);
            }

            await task;
        }

        public void Dispose()
        {
            if (liteDatabase != null)
            {
                liteDatabase.Dispose();
                liteDatabase = null;
            }
        }
    }
}
