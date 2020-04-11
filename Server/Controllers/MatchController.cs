using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Server.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MatchController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger _logger;
        private const int InitialRating = 2000;
        private const int NumberOfTopPlayers = 100;
        private const int MaximumDelta = 25;
        private const int BaseRating = 30;
        private const int DivisionPoints = 40;

        public MatchController(AppDbContext context, ILogger<MatchController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost]
        [Route("auto-pick")]
        public async Task<ActionResult<AutoPickResponse>> AutoPick(AutoPickRequest request)
        {
            var realSteamIds = request.Players.Select(ulong.Parse).ToList();
            var selectedHeroes = request.SelectedHeroes;
            var players = await _context.Players
                .Where(p => realSteamIds.Contains(p.SteamId))
                .Select(p => new
                {
                    SteamId = p.SteamId.ToString(),
                    HeroesMap = p.Matches
                        .Where(m => m.Match.MapName == request.MapName)
                        .OrderByDescending(m => m.MatchId)
                        .Take(100)
                        .Select(m => m.Hero)
                        .ToList(),
                    HeroesGlobal = p.Matches
                        .OrderByDescending(m => m.MatchId)
                        .Take(100)
                        .Select(m => m.Hero)
                        .ToList(),
                })
                .ToListAsync();

            List<string> GetBestHeroes(List<string> heroes) => heroes
                .Except(selectedHeroes)
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList();

            return new AutoPickResponse()
            {
                Players = players.Select(p =>
                {
                    var bestHeroesOnMap = GetBestHeroes(p.HeroesMap);
                    var bestHeroesGlobal = GetBestHeroes(p.HeroesGlobal);

                    return new AutoPickResponse.Player()
                    {
                        SteamId = p.SteamId.ToString(),
                        Heroes = (bestHeroesOnMap.Count() >= 3 ? bestHeroesOnMap : bestHeroesGlobal)
                            .Take(3)
                            .ToList(),
                    };
                })
            };
        }

        [HttpPost]
        [Route("before")]
        public async Task<BeforeMatchResponse> Before(BeforeMatchRequest request)
        {
            var customGame = request.CustomGame.Value;
            var mapName = request.MapName;

            var realSteamIds = request.Players.Select(ulong.Parse).ToList();
            // We need to do another call in order to get the top players for leaderBoard
            List<LeaderBoardPlayer> topPlayers = await GetTopPlayers();
            var topPlayerIds = topPlayers.Select(tp => tp.SteamId).ToList();
            var responses = await _context.Players
                .Where(p => realSteamIds.Contains(p.SteamId) && !topPlayerIds.Contains(p.SteamId))
                .Select(p => new
                {
                    SteamId = p.SteamId.ToString(),
                    Patreon =
                        new BeforeMatchResponse.Patreon()
                        {
                            EndDate = p.PatreonEndDate,
                            Level = p.PatreonLevel,
                            EmblemEnabled = p.PatreonEmblemEnabled ?? true,
                            EmblemColor = p.PatreonEmblemColor ?? "White",
                            BootsEnabled = p.PatreonBootsEnabled ?? true,
                            ChatWheelFavorites = p.PatreonChatWheelFavorites ?? new List<int>(),
                        },
                    Matches = p.Matches
                        .Where(m => m.Match.CustomGame == customGame)
                        .OrderByDescending(m => m.MatchId)
                        .Select(mp => new
                        {
                            mp.Kills,
                            mp.Deaths,
                            mp.Assists,
                            mp.Match.MapName,
                            mp.PickReason,
                            mp.Hero,
                            IsWinner = mp.Team == mp.Match.Winner,
                        })
                        .ToList(),
                    p.Rating
                })
                .ToListAsync();

            return new BeforeMatchResponse()
            {
                Players = request.Players
                    .Select(id =>
                    {
                        var response = responses.FirstOrDefault(p => p.SteamId == id);
                        if (response == null)
                        {
                            return new BeforeMatchResponse.Player()
                            {
                                SteamId = id.ToString(),
                                Patreon = new BeforeMatchResponse.Patreon()
                                {
                                    Level = 0,
                                    EmblemEnabled = true,
                                    EmblemColor = "White",
                                    BootsEnabled = true,
                                    ChatWheelFavorites = new List<int>(),
                                },
                                SmartRandomHeroesError = "no_stats",
                            };
                        }

                        if (response.Patreon.EndDate < DateTime.UtcNow)
                        {
                            response.Patreon.Level = 0;
                        }

                        var matchesOnMap = response.Matches.Where(m => m.MapName == mapName).ToList();

                        var player = new BeforeMatchResponse.Player
                        {
                            SteamId = id.ToString(),
                            Patreon = response.Patreon,
                            Streak = matchesOnMap.TakeWhile(w => w.IsWinner).Count(),
                            BestStreak = matchesOnMap.LongestStreak(w => w.IsWinner),
                            AverageKills = matchesOnMap
                                .Select(x => (double)x.Kills)
                                .DefaultIfEmpty()
                                .Average(),
                            AverageDeaths = matchesOnMap
                                .Select(x => (double)x.Deaths)
                                .DefaultIfEmpty()
                                .Average(),
                            AverageAssists = matchesOnMap
                                .Select(x => (double)x.Assists)
                                .DefaultIfEmpty()
                                .Average(),
                            Wins = matchesOnMap.Count(w => w.IsWinner),
                            Loses = matchesOnMap.Count(w => !w.IsWinner),
                        };

                        List<string> GetSmartRandomHeroes(bool onMap)
                        {
                            var matches = (onMap ? matchesOnMap : response.Matches).Where(m => m.PickReason == "pick");
                            return matches
                                .Take(100)
                                .GroupBy(m => m.Hero)
                                .Where(g => g.Count() >= (int)Math.Ceiling(Math.Min(matches.Count(), 100) / 20.0))
                                .Select(g => g.Key)
                                .ToList();
                        };

                        var smartRandomHeroesMap = GetSmartRandomHeroes(true);
                        var smartRandomHeroesGlobal = GetSmartRandomHeroes(false);

                        var heroes = smartRandomHeroesMap.Count >= 5
                            ? smartRandomHeroesMap
                            : smartRandomHeroesGlobal;

                        if (heroes.Count >= 3)
                        {
                            player.SmartRandomHeroes = heroes;
                        }
                        else
                        {
                            player.SmartRandomHeroesError = "no_stats";
                        }

                        return player;
                    })
                    .ToList(),
                LeaderBoard = topPlayers
                    .Union(responses.Select(x => new LeaderBoardPlayer()
                    {
                        Rating = x.Rating,
                        SteamId = ulong.Parse(x.SteamId)
                    }))
                    .OrderByDescending(lp => lp.Rating)
            };
        }

        [HttpPost]
        [Route("after")]
        public async Task<AfterMatchResponse> After([FromBody] AfterMatchRequest request)
        {
            var requestedSteamIds = request.Players.Select(p => ulong.Parse(p.SteamId)).ToList();

            var existingPlayers = await _context.Players
                .Where(p => requestedSteamIds.Contains(p.SteamId))
                .ToListAsync();

            var newPlayers = request.Players
                .Where(r => existingPlayers.All(p => p.SteamId.ToString() != r.SteamId))
                .Select(p => new Player() { SteamId = ulong.Parse(p.SteamId), Rating = InitialRating })
                .ToList();

            foreach (var playerUpdate in request.Players.Where(p => p.PatreonUpdate != null))
            {
                var player =
                    existingPlayers.FirstOrDefault(p => p.SteamId.ToString() == playerUpdate.SteamId) ??
                    newPlayers.FirstOrDefault(p => p.SteamId.ToString() == playerUpdate.SteamId);
                // TODO: Shouldn't be the case ever?
                if (player == null) continue;

                player.PatreonBootsEnabled = playerUpdate.PatreonUpdate.BootsEnabled;
                player.PatreonEmblemEnabled = playerUpdate.PatreonUpdate.EmblemEnabled;
                player.PatreonEmblemColor = playerUpdate.PatreonUpdate.EmblemColor;
                player.PatreonChatWheelFavorites = playerUpdate.PatreonUpdate.ChatWheelFavorites;
            }

            var match = new Match
            {
                CustomGame = request.CustomGame.Value,
                MatchId = request.MatchId,
                MapName = request.MapName,
                Winner = request.Winner,
                Duration = request.Duration,
                EndedAt = DateTime.UtcNow
            };

            match.Players = request.Players
                .Select(p => new MatchPlayer
                {
                    Match = match,
                    SteamId = ulong.Parse(p.SteamId),
                    PlayerId = p.PlayerId,
                    Team = p.Team,
                    Hero = p.Hero,
                    PickReason = p.PickReason,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Assists = p.Assists,
                    Level = p.Level,
                })
                .ToList();

            _context.AddRange(newPlayers);
            _context.Matches.Add(match);
            await _context.SaveChangesAsync();

            var allPlayers = await UpdateNewRating(request, existingPlayers, newPlayers);
            AfterMatchResponse response = await GetNewLeaderBoard(allPlayers);
            return response;
        }

        [HttpPost]
        [Route("events")]
        public async Task<List<object>> Events([FromBody] MatchEventsRequest request)
        {
            var matchId = request.MatchId;
            var events = await _context.MatchEvents.Where(e => e.MatchId == matchId).ToListAsync();

            _context.MatchEvents.RemoveRange(events);
            await _context.SaveChangesAsync();

            return events.Select(e => e.Body).ToList();
        }

        /// <summary>
        /// Should return the top 100 top players. If you need to change that, change the constant at the beginning
        /// </summary>
        /// <returns></returns>
        private async Task<List<LeaderBoardPlayer>> GetTopPlayers()
        {
            return await _context.Players
                            .OrderByDescending(p => p.Rating)
                            .Take(NumberOfTopPlayers)
                            .Select(p => new LeaderBoardPlayer
                            {
                                SteamId = p.SteamId,
                                Rating = p.Rating
                            }).ToListAsync();
        }

        /// <summary>
        /// It gets winning teams, losing teams and then updates the new rating
        /// </summary>
        /// <param name="request"></param>
        /// <param name="existingPlayers"></param>
        /// <param name="newPlayers"></param>
        /// <returns></returns>
        private async Task<List<Player>> UpdateNewRating(AfterMatchRequest request, List<Player> existingPlayers, List<Player> newPlayers)
        {
            var allPlayers = existingPlayers.Union(newPlayers).ToList();
            var winningTeam = GetWinningTeam(request, allPlayers, out var averageWinningRating);
            var losingTeam = GetLosingTeam(request, allPlayers, out var averageLosingRating);
            var scoreDelta = CalculateScoreDelta(averageWinningRating, averageLosingRating);
            await UpdateRating(winningTeam, losingTeam, scoreDelta);
            return allPlayers;
        }


        /// <summary>
        /// Formula is the difference between loosing and winning team of avg rating divided by 40
        /// </summary>
        /// <param name="averageWinningRating"></param>
        /// <param name="averageLosingRating"></param>
        /// <returns></returns>
        private static int CalculateScoreDelta(double averageWinningRating, double averageLosingRating)
        {
            var scoreDeltaDouble =
                -(averageWinningRating - averageLosingRating) /
                DivisionPoints; // e.g. - (1900 - 2100) / 40 = 5. Meaning winning team will get 5 more points 
                                // than the base as their average was weaker
            int scoreDelta =
                Convert.ToInt32(Math.Round(scoreDeltaDouble, 0,
                    MidpointRounding.AwayFromZero)); // we don't do a conversion straight to the double
                                                     // as it will do a MidpointRounding.ToEven by default
            scoreDelta = scoreDelta > MaximumDelta ? MaximumDelta : scoreDelta;
            return scoreDelta;
        }

        private static IEnumerable<Player> GetLosingTeam(AfterMatchRequest request, List<Player> allPlayers, out double averageLosingRating)
        {
            var losingTeamIds = request
                .Players
                .Where(p => p.Team != request.Winner)
                .Select(p => ulong.Parse(p.SteamId))
                .ToList();
            var losingTeam = allPlayers.Where(p => losingTeamIds.Contains(p.SteamId));
            averageLosingRating = losingTeam.Average(p => p.Rating);
            return losingTeam;
        }

        private static IEnumerable<Player> GetWinningTeam(AfterMatchRequest request, List<Player> allPlayers, out double averageWinningRating)
        {
            var winningTeamIds = request
                .Players
                .Where(p => p.Team == request.Winner)
                .Select(p => ulong.Parse(p.SteamId))
                .ToList();
            var winningTeam = allPlayers.Where(p => winningTeamIds.Contains(p.SteamId));
            averageWinningRating = winningTeam.Average(p => p.Rating);
            return winningTeam;
        }

        private async Task UpdateRating(IEnumerable<Player> winningTeam, IEnumerable<Player> losingTeam, int scoreDelta)
        {
            foreach (var player in winningTeam)
                player.Rating = player.Rating + (BaseRating + scoreDelta);
            foreach (var player in losingTeam)
                player.Rating = player.Rating - (BaseRating + scoreDelta);
            _context.UpdateRange(winningTeam);
            _context.UpdateRange(losingTeam);
            await _context.SaveChangesAsync();
        }

        private async Task<AfterMatchResponse> GetNewLeaderBoard(IEnumerable<Player> players)
        {
            var response = new AfterMatchResponse();
            var topPlayers = await GetTopPlayers();
            var topPlayersId = topPlayers.Select(tp => tp.SteamId).ToList();
            var leaderBoardMatchPlayers = players
                .Where(p => !topPlayersId.Contains(p.SteamId))
                .Select(p => new LeaderBoardPlayer()
                {
                    Rating = p.Rating,
                    SteamId = p.SteamId
                });
            topPlayers.AddRange(leaderBoardMatchPlayers);
            response.LeaderBoard = topPlayers
                .OrderByDescending(p => p.Rating);
            return response;
        }
    }

    public class AfterMatchRequest
    {
        [Required] public CustomGame? CustomGame { get; set; }
        [Required] public long MatchId { get; set; }
        [Required] public string MapName { get; set; }
        [Required] public ushort Winner { get; set; }
        [Required] public uint Duration { get; set; }

        [Required] public IEnumerable<Player> Players { get; set; }

        public class Player
        {
            [Required] public ushort PlayerId { get; set; }
            [Required] public string SteamId { get; set; }
            [Required] public ushort Team { get; set; }
            [Required] public string Hero { get; set; }
            [Required] public string PickReason { get; set; }
            [Required] public uint Kills { get; set; }
            [Required] public uint Deaths { get; set; }
            [Required] public uint Assists { get; set; }
            [Required] public uint Level { get; set; }
            // TODO: We don't store it anymore
            [Required] public List<object> Items { get; set; }
            public PatreonUpdate PatreonUpdate { get; set; }
        }

        public class PatreonUpdate
        {
            public bool EmblemEnabled { get; set; }
            public string EmblemColor { get; set; }
            public bool BootsEnabled { get; set; }
            // TODO: Required?
            public List<int>? ChatWheelFavorites { get; set; }
        }
    }

    public class AutoPickRequest
    {
        [Required] public string MapName { get; set; }
        [Required] public List<string> SelectedHeroes { get; set; }
        [Required] public List<string> Players { get; set; }
    }

    public class AutoPickResponse
    {
        public IEnumerable<Player> Players { get; set; }

        public class Player
        {
            public string SteamId { get; set; }
            public List<string> Heroes { get; set; }
        }
    }

    public class BeforeMatchRequest
    {
        [Required] public CustomGame? CustomGame { get; set; }
        [Required] public string MapName { get; set; }
        [Required] public List<string> Players { get; set; }
    }

    public class BeforeMatchResponse
    {
        public IEnumerable<Player> Players { get; set; }
        public IEnumerable<LeaderBoardPlayer> LeaderBoard { get; set; }

        public class Player
        {
            public string SteamId { get; set; }
            public List<string> SmartRandomHeroes { get; set; }
            public string SmartRandomHeroesError { get; set; }
            public int Streak { get; set; }
            public int BestStreak { get; set; }
            public double AverageKills { get; set; }
            public double AverageDeaths { get; set; }
            public double AverageAssists { get; set; }
            public int Wins { get; set; }
            public int Loses { get; set; }
            public Patreon Patreon { get; set; }
        }

        public class Patreon
        {
            public DateTime? EndDate { get; set; }
            public ushort Level { get; set; }
            public bool EmblemEnabled { get; set; }
            public string EmblemColor { get; set; }
            public bool BootsEnabled { get; set; }
            public List<int> ChatWheelFavorites { get; set; }
        }
    }

    public class MatchEventsRequest
    {
        [Required] public long MatchId { get; set; }
    }

    public class AfterMatchResponse
    {
        public IEnumerable<LeaderBoardPlayer> LeaderBoard { get; set; }
    }

    public class LeaderBoardPlayer
    {
        public ulong SteamId { get; set; }
        public int Rating { get; set; }
    }
}
