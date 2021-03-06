using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using PokemonPocket.Models.Multiplayer;

/// <summary>
/// Additional comprehensive creative feature.
/// Transient service providing MULTIPLAYER battle functionality.
/// Each pokemon can attack from 1 up to 100 dmg per tick.
/// The winner gets anywhere from 1-100 XP.
/// Each instance refers to one user.
/// TODO: Comprehensive debugging
/// Critical TODO: Implement GUID-based Pokemon IDs! Necessary!
/// </summary>
namespace PokemonPocket {
    /// <summary>
    /// Assumes HTTP protocol; change as needed
    /// </summary>
    public class MPBattleService {
        private HttpClient client;
        private string uuid;
        private string myPokemonUuid;
        public MPBattleResults MetaResults { get; private set; }

        // todo: try catch on call for this
        public MPBattleService(string serverHost) {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            client = new HttpClient(handler);
            client.BaseAddress = new Uri($"http://{serverHost}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }
        /// <summary>
        /// Returns success of joining battle (is another battle ongoing?)
        /// </summary>
        public async Task<bool> JoinBattle(Pokemon pokemon) {
            myPokemonUuid = pokemon.Uuid;
            StringContent content = new StringContent(JsonConvert.SerializeObject(pokemon),
                                                      Encoding.UTF8,
                                                      "application/json");
            HttpResponseMessage res = await client.PostAsync("/api/Battle", content);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return false;

            // uuid = res.Content.ReadAsStringAsync().Result;  // broken: double-escaped strings
            uuid = await res.Content.ReadAsAsync<string>(); // Microsoft.AspNet.WebApi.Client
            return true;
        }

        /// <summary>
        /// Keep polling every 500ms until battle results are released
        /// After this, get meta-battle results from property MetaResults
        /// </summary>
        public async Task<List<MPBattleTick>> GetBattleResults() {
            if (uuid == null)
                return null;

            List<MPBattleTick> battleResults = null;
            while (battleResults == null) {
                HttpResponseMessage res = await client.GetAsync($"/api/battle/{uuid}");
                if (res.StatusCode == HttpStatusCode.OK) {
                    string jsonStr = await res.Content.ReadAsStringAsync();
                    battleResults = JsonConvert.DeserializeObject<List<MPBattleTick>>(jsonStr);
                    break;
                }
                await Task.Delay(500);  // keep polling on delay to avoid unintentional DoS attack
            }

            // Check if won or lost this battle
            bool win = false;
            int winnerExpGain = 0;
            string winnerUuid = battleResults.Last().AttackerUuid;
            using (PokemonDbContext dbctx = new PokemonDbContext()) {
                Pokemon p = (from pokemon in dbctx.Pokemons
                             where pokemon.Uuid == winnerUuid
                             select pokemon
                            ).FirstOrDefault();
                if (p != null) {
                    if (winnerUuid == myPokemonUuid) {  // avoid issues with copying program over
                        win = true;

                        // If won, apply random XP
                        Random rand = new Random();
                        winnerExpGain = rand.Next(1, 100 + 1);
                        p.Exp += winnerExpGain;
                        dbctx.SaveChanges();
                    }
                }
            }


            // Apply damage dealt
            foreach (MPBattleTick tick in battleResults) {
                // Get opponent pokemon by id
                using (PokemonDbContext dbctx = new PokemonDbContext()) {
                    Pokemon p = (from pokemon in dbctx.Pokemons
                                 where pokemon.Uuid == tick.OpponentUuid
                                 select pokemon
                                ).FirstOrDefault();

                    // If exists, and damage is to our pokemon (in case of copied DB), apply damage dealt
                    if (p != null) {
                        if (p.Uuid == myPokemonUuid) {
                            p.Hp = tick.OpponentHealthAfterTick;
                            dbctx.SaveChanges();
                        }
                    }
                }
            }

            MetaResults = new MPBattleResults
            {
                win = win,
                exp = winnerExpGain
            };

            return battleResults;
        }
    }

    public struct MPBattleResults {
        public bool win;
        public int exp;
    }
}