using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using static SHC.Constants;

namespace SHC
{
    class GreatestLord
    {
        readonly static Dictionary<string, Dictionary<string, Dictionary<string, string>>> playerData;
        readonly static Dictionary<string, object> statsDictionary;
        public static List<Player> PlayerList { get; }
        public static List<List<Dictionary<string, object>>> PlayerHistory { get;}

        static GreatestLord()
        {
            statsDictionary = new Dictionary<string, object>();
            PlayerHistory = new List<List<Dictionary<string, object>>>();
            playerData = 
                JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(File.ReadAllText("memory/greatestlord.json"));
        }

        public static Dictionary<string, object> Update(LinkedList<Dictionary<string, object>> endingPlayerStats)
        {
            Dictionary<string, int> scoreDict = new Dictionary<string, int>();
            scoreDict["Gold"] = 0;
            scoreDict["WeightedTroopsKilled"] = 0;
            scoreDict["LordKills"] = 0;
            scoreDict["MapStartYear"] = 0;
            scoreDict["MapStartMonth"] = 0;
            scoreDict["MapEndYear"] = 0;
            scoreDict["MapEndMonth"] = 0;
            scoreDict["WeightedBuildingsDestroyed"] = 0;

            Dictionary<string, object> mapStats = new Dictionary<string, object>();

            foreach (KeyValuePair<string, Dictionary<string, string>> entry in playerData["Map"])
            {
                int addr = Convert.ToInt32(entry.Value["address"], 16);
                object value = Reader.ReadType(addr, entry.Value["type"].ToString());
                mapStats[entry.Key] = value;
                try
                {
                    scoreDict[entry.Key] = Convert.ToInt32(value);
                }
                catch (Exception)
                {
                    continue;
                }
            }
            if ((int)mapStats["MapStartYear"] == 0)
            {
                return statsDictionary;
            }

            statsDictionary["Map"] = mapStats;

            List<Dictionary<string, object>> playerStats = new List<Dictionary<string, object>>();
            for (var i = 0; i < MAX_PLAYERS; i++)
            {
                Dictionary<string, object> currentPlayer = new Dictionary<string, object>();
                currentPlayer["PlayerNumber"] = i + 1;
                foreach (KeyValuePair<string, Dictionary<string, string>> entry in playerData["Player"])
                {
                    int addr = Convert.ToInt32(entry.Value["address"], 16) + Convert.ToInt32(entry.Value["offset"], 16) * i;
                    string type = entry.Value["type"];

                    object value = Reader.ReadType(addr, type);
                    currentPlayer[entry.Key] = value;

                    if (entry.Key == "Active" && value.ToString().ToLowerInvariant() == "false")
                    {
                        break;
                    }

                    try
                    {
                        scoreDict[entry.Key] = Convert.ToInt32(value);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
                if (currentPlayer["Active"].ToString().ToLowerInvariant() == "false")
                {
                    continue;
                }

                foreach (var player in endingPlayerStats)
                {
                    if ((int)player["PlayerNumber"] == (int)currentPlayer["PlayerNumber"])
                    {
                        currentPlayer["EconomyScore"] = player["EconomyScore"];
                        currentPlayer["MilitaryScore"] = player["MilitaryScore"];
                        currentPlayer["Score"] = player["Score"];
                        currentPlayer["LargestWeightedArmy"] = player["LargestWeightedArmy"];
                        currentPlayer["LargestArmy"] = player["LargestArmy"];
                    }
                }

                currentPlayer["VanillaScore"] = 
                    GreatestLord.CalculateScore(scoreDict["Gold"], scoreDict["LordKills"], scoreDict["WeightedTroopsKilled"], 
                    scoreDict["WeightedBuildingsDestroyed"], scoreDict["MapStartYear"], scoreDict["MapStartMonth"],
                    scoreDict["MapEndYear"], scoreDict["MapEndMonth"]);
                
                //Matchtime only gets loaded correctly for player 0 (because its not supposed to have an offset)
                //gets fixed here and every player gets their own copy of matchtime
                if (i > 0) { currentPlayer["Matchtime"] = playerStats[0]["Matchtime"]; }

                playerStats.Add(currentPlayer);
                //add income to playerstats:
                addTaxes(i, ref playerStats);
                addEcoScore(i, ref playerStats);
                calculateNormedIncome(i, ref playerStats);
            }

            statsDictionary["PlayerStatistics"] = playerStats;
            PlayerHistory.Add(playerStats);

            //Debug Output
            Console.WriteLine(playerStats[0]["Income"]);

            return statsDictionary;
        }

        public static long CalculateScore
            (int gold, int lordKills, int weightedKills, int weightedBuildings, int startYear, int startMonth, int endYear, int endMonth)
        {
            const long multiplier = 0x66666667;
            long goldBonus = ((gold * multiplier) >> 32) / 4;
            long score = goldBonus + weightedKills + weightedBuildings * 100;
            score = score + (score * lordKills) / 4;

            int dateBonus = (endYear - startYear) * 12;
            dateBonus -= startMonth;
            dateBonus += endMonth;

            if (dateBonus < 1)
            {
                dateBonus = 1;
            }
            int bonusDivider = 200 + dateBonus;

            score = score * 200;
            score = score / bonusDivider;
            return score;
        }

        public static void calculateNormedIncome(int player, ref List<Dictionary<string,object>> playerStats)
        {
            if (PlayerHistory.Count == 0)
            {
                playerStats[player]["Income"] = 0;
                return;
            }

            int currentTime = Convert.ToInt32(playerStats[0]["Matchtime"]);
            double playerIncome = 0;
            //calculate time point 3months in the past
            int index = PlayerHistory.Count - 1;
            int pastTime = currentTime;

            while (pastTime > Math.Max(0, currentTime - 2400))
            {
                pastTime = Convert.ToInt32(PlayerHistory[index][0]["Matchtime"]);
                if(index <= 0) { break; }
                index--;
            }


            //calculate score difference and norm to 3 months
            int timediff = currentTime - pastTime;
            if(timediff != 0)
            {
                int currentScore = Convert.ToInt32(PlayerHistory[PlayerHistory.Count - 1][player]["EcoScore"]);
                int pastScore = Convert.ToInt32(PlayerHistory[index][player]["EcoScore"]);
                playerIncome = (Math.Abs(currentScore - pastScore)*2400)/Math.Abs(timediff); //some weird rounding happens
            }
            else
            {
                playerIncome = 0;
            }
            playerStats[player]["Income"] = playerIncome;

        }

         public static int weightedResources(int player, ref List<Dictionary<string,object>> playerStats)
         {
             int erg = 0;
            erg += Convert.ToInt32(playerStats[player]["Wood"]) * 2;
            erg += Convert.ToInt32(playerStats[player]["Stone"]) * 5;
            erg += Convert.ToInt32(playerStats[player]["Iron"]) * 26;
            erg += Convert.ToInt32(playerStats[player]["Pitch"]) * 18;
            erg += Convert.ToInt32(playerStats[player]["Food"]) * 4;
            erg += Convert.ToInt32(playerStats[player]["Weapons"]) * 15;
            return erg;
         }

        public static void addEcoScore(int player, ref List<Dictionary<string, object>> playerStats)
        {
            playerStats[player]["EcoScore"] = weightedResources(player, ref playerStats) + Convert.ToInt32(playerStats[player]["Taxes"]);
        }

        public static void clearHistory()
        {
            PlayerHistory.Clear();
        }

        public static double taxesThisTick(int player, ref List<Dictionary<string, object>> playerStats, int lastTime, int currentTime)
        {
            double[] taxesPerPerson = { -0.8, -0.6, -0.4, 0, 0.6, 0.8, 1, 1.2, 1.4, 1.6, 1.8, 2 };
            int taxSetting = Convert.ToInt32(playerStats[player]["TaxSetting"]);
            Console.WriteLine("Taxsetting " + taxSetting);
            Console.WriteLine("Pop " + Convert.ToInt32(playerStats[player]["Population"]));
            int deltaTime = currentTime - lastTime;
            Console.WriteLine("delta " + deltaTime);
            double taxIncomeCurrent = (taxesPerPerson[taxSetting] * Convert.ToInt32(playerStats[player]["Population"]) * deltaTime) / 800;
            Console.WriteLine("Current " + taxIncomeCurrent);
            return taxIncomeCurrent;
        }

        public static void addTaxes(int player , ref List<Dictionary<string, object>> playerStats)
        {
            if (PlayerHistory.Count == 0)
            {
                playerStats[player]["Taxes"] = 0;
                return;
            }
            int lastTime = Convert.ToInt32(PlayerHistory[PlayerHistory.Count - 1][0]["Matchtime"]);
            int currentTime = Convert.ToInt32(playerStats[0]["Matchtime"]);
            playerStats[player]["Taxes"] = Convert.ToInt32(PlayerHistory[PlayerHistory.Count - 1][player]["Taxes"]) + taxesThisTick(player, ref playerStats, lastTime, currentTime);

        }
    }
}
