using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using EkInstaStoryGenerator.ApiClasses;
using InstagramApiSharp.API.Builder;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Enums;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EkInstaStoryGenerator;

class Program
{
    private static Dictionary<DateTime, List<(string EkTeam, string OpTeam, bool IsHomeGame, bool Fuck, string Score)>> gameDayResults =
        new();
    private static Dictionary<DateTime, List<(string EkTeam, string OpTeam, bool IsHomeGame, bool Fuck, string Time)>> gameDays = new();

    private static readonly string request =
        "https://www.basketball-bund.net/rest/club/id/886/actualmatches?justHome=false&rangeDays=3";

    private static Brush brushWhite = Brushes.Solid(Color.WhiteSmoke);
    private static Brush brushBlack = Brushes.Solid(Color.Black);

    private static readonly int padAfterHeader = 40;
    private static readonly int padAfterGame = 10;
    private static readonly float padRect = 10;
    private static readonly float padBetweenRects = 20;
    
    private static readonly FontCollection fonts = new();
    private static readonly string fontFamily = "Roboto";
    
    private static Font titleFont;
    private static Font headerFont;
    private static Font standardFont;
    private static Font boldFont;

    public static MemoryStream? Stream;
    
    static Program()
    {
        fonts.Add(GetResource("EkInstaStoryGenerator.Resources.Roboto-Bold.ttf"));
        fonts.Add(GetResource("EkInstaStoryGenerator.Resources.Roboto-Regular.ttf"));
        
        // create fonts
        titleFont = fonts.Get(fontFamily).CreateFont(122, FontStyle.Bold);
        headerFont = fonts.Get(fontFamily).CreateFont(52, FontStyle.Bold);
        standardFont = fonts.Get(fontFamily).CreateFont(32);
        boldFont = fonts.Get(fontFamily).CreateFont(32, FontStyle.Bold);
    }
    
    static async Task Main(string[] args)
    {
        if (args.Length > 1)
        {
            Console.WriteLine("Usage: EkInstaStoryGenerator [-results]");
            return;
        }

        await LoadAsync();

        if (args.Length == 0)
        {
            var data = gameDays.Where(d => d.Key - DateTime.Today <= TimeSpan.FromDays(2))
                .ToDictionary(x => x.Key, x => x.Value);
            if (data.Count == 0)
            {
                return;
            }
            await Render("SPIELTAG", data);
        }
        else if (args[0] == "-results")
        {
            var data = gameDayResults.Where(d => DateTime.Today - d.Key <= TimeSpan.FromDays(2))
                .ToDictionary(x => x.Key, x => x.Value);
            if (data.Count == 0)
            {
                return;
            }
            await Render("ERGEBNISSE", data);
        }
        else
        {
            return;
        }

        FileInfo info = new(Path.Combine(AppContext.BaseDirectory, "user.json"));
        if (!info.Exists)
        {
            Console.WriteLine("No user.json");
            return;
        }
        
        var user = await JsonSerializer.DeserializeAsync(info.OpenRead(), SourceGenerationContext.Default.User);
        
        if (user is null)
        {
            Console.WriteLine("Wrong format for user.json");
            return;
        }
        
        UserSessionData userSession = new() { UserName = user.UserName, Password = user.Password };
        
        var delay = RequestDelay.FromSeconds(2, 2);
        var instaApi = InstaApiBuilder.CreateBuilder()
            .SetUser(userSession)
            .SetRequestDelay(delay)
            .Build();
        
        instaApi.SetApiVersion(InstaApiVersionType.Version261);
        
        string stateFile = Path.Combine(AppContext.BaseDirectory, "state.bin");
        try
        {
            if (File.Exists(stateFile))
            {   
                await instaApi.LoadStateDataFromStringAsync(await File.ReadAllTextAsync(stateFile));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        
        if (!instaApi.IsUserAuthenticated)
        {
            // login
            Console.WriteLine($"Logging in as {userSession.UserName}");
            delay.Disable();
            var logInResult = await instaApi.LoginAsync();
            delay.Enable();
            if (!logInResult.Succeeded)
            {
                Console.WriteLine($"Unable to login: {logInResult.Info.Message}");
                return;
            }
        }
        
        await File.WriteAllTextAsync(stateFile, await instaApi.GetStateDataAsStringAsync());

        if (Stream is null)
        {
            return;
        }
        
        Stream.Position = 0;
        await using (var s = File.Create("temp.png"))
        {
            await Stream.CopyToAsync(s);     
        }
        
        var image = new InstaImage(new FileInfo("temp.png").FullName, 1080, 1920);

        var result = await instaApi.StoryProcessor.UploadStoryPhotoAsync(image, "someawesomepicture");
        Console.WriteLine(result.Succeeded
            ? $"Story created: {result.Value.Media.Pk}"
            : $"Unable to upload photo story: {result.Info.Message}");
    }

    private static async Task LoadAsync()
    {
        // clear previous data
        gameDayResults.Clear();
        gameDays.Clear();
        
        HttpClient client = new();

        string uri;
        if (OperatingSystem.IsBrowser())
        {
            // we need to bypass cors
            uri = $"https://cors-test.jonathan-kopmann.workers.dev/?{request}";
        }
        else
        {
            uri = request;
        }

        var response = await client.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            var t = await response.Content.ReadAsStringAsync();
            // TODO: show some error
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        ApiResponse<ClubData>? c = JsonSerializer.Deserialize(responseBody, SourceGenerationContext.Default.ApiResponseClubData);
        if (c is null)
        {
            // TODO: show some error
            return;
        }

        // go through all games returned by the api and add them to our dictionaries
        foreach (var match in c.Data.Matches)
        {
            // we want to check for games that are in the future, so we need the exact one with the time and the one without for grouping per day
            DateTime gameTimeExact = DateTime.ParseExact(match.KickoffDate + match.KickoffTime, c.DateFormat + c.TimeFormatShort, null);
            DateTime gameTime = DateTime.ParseExact(match.KickoffDate, c.DateFormat, null);

            var homeName = GetTeamName(match.HomeTeam, match.LigaData, c.Data);
            var guestName = GetTeamName(match.GuestTeam, match.LigaData, c.Data);
            
            var ekTeam = homeName.Item2 ? homeName.Item1 : guestName.Item1;
            var opTeam = homeName.Item2 ? guestName.Item1 : homeName.Item1;

            if (gameTimeExact < DateTime.Now)
            {
                gameDayResults.TryAdd(gameTime, new());

                string? result = match.Result;
                if (string.IsNullOrEmpty(result))
                {
                    result = "-:-";
                }
                else if (!homeName.Item2)
                {
                    result = string.Join(":", result.Split(':').Reverse());
                }
                
                gameDayResults[gameTime].Add((ekTeam, opTeam, homeName.Item2,
                    match.Abgesagt == true || match.Verzicht == true, result));
            }
            else
            {
                gameDays.TryAdd(gameTime, new());
                gameDays[gameTime].Add((ekTeam, opTeam, homeName.Item2,
                    match.Abgesagt == true || match.Verzicht == true, match.KickoffTime + " Uhr"));
            }
        }
    }

    private static (string, bool) GetTeamName(Team team, LigaData ligaData, ClubData c)
    {
        if (team.ClubId == c.Club.VereinId)
        {
            int n;
            if (ligaData.AkName == "Senioren")
            {
                if (!int.TryParse(team.Teamname[^1..], out n))
                {
                    n = 1;
                }

                return (ligaData.Geschlecht == "weiblich" ? $"D{n}" : $"H{n}", true);
            }

            string extra = string.Empty;
            if (int.TryParse(team.Teamname[^1..], out n))
            {
                extra = $" {n}";
            }
            
            return (ligaData.AkName + (ligaData.Geschlecht == "weiblich" ? "w" : string.Empty) + extra, true);
        }

        return (team.Teamname, false);
    }

    private static async Task Render(string title, Dictionary<DateTime, List<(string EkTeam, string OpTeam, bool IsHome, bool Fuck, string ScoreOrTime)>> data)
    {
        // load our background
        using var backgroundImage = await LoadImageAsync("EkInstaStoryGenerator.Resources.template.png");
        using var outputImage = new Image<Rgba32>(backgroundImage.Width, backgroundImage.Height);
        
        // draw the background
        outputImage.Mutate(x => x.DrawImage(backgroundImage, 1));

        // create rectangles that fit the largest text
        FontRectangle ekRect = data.Values.SelectMany(gameDay => gameDay).Aggregate(FontRectangle.Empty,
            (current, game) => FontRectangle.Union(current,
                TextMeasurer.MeasureAdvance(game.EkTeam, new TextOptions(boldFont)))).Pad(padRect);
        FontRectangle opRect = data.Values.SelectMany(gameDay => gameDay).Aggregate(FontRectangle.Empty,
            (current, game) => FontRectangle.Union(current,
                TextMeasurer.MeasureAdvance(game.OpTeam, new TextOptions(standardFont)))).Pad(padRect);
        FontRectangle atRect = TextMeasurer.MeasureAdvance("vs", new TextOptions(boldFont)).Pad(padRect);
        FontRectangle timeRect = data.Values.SelectMany(gameDay => gameDay).Aggregate(FontRectangle.Empty,
            (current, game) => FontRectangle.Union(current,
                TextMeasurer.MeasureAdvance(game.ScoreOrTime, new TextOptions(boldFont)))).Pad(padRect);
        
        // use the rects to calculate the offset of the x and y axis, so the stuff is centered
        float offsetX = (outputImage.Width - (ekRect.Width + atRect.Width + opRect.Width + timeRect.Width +
                                              3 * padBetweenRects)) / 2;
        float offsetY = (outputImage.Height - (data.Values.Count * (headerFont.Size + padAfterHeader) +
                                               (data.Values.Count - 1) * padAfterGame + data.Values.Sum(va =>
                                                   va.Count * ekRect.Height + (va.Count - 1) * padAfterGame))) / 2;

        // draw title at the top
        outputImage.Mutate(x => x.DrawCenteredText(title, titleFont, brushWhite, new PointF(outputImage.Width / 2, offsetY / 2), true));

        float yPos = offsetY;
        foreach (var day in data)
        {
            // draw day
            string s = day.Key.ToString("dddd dd.MM.yyyy", new CultureInfo("de-DE")).ToUpper();
            outputImage.Mutate(x =>
                x.DrawCenteredText(s, headerFont, brushWhite, new PointF(outputImage.Width / 2, yPos)));
            yPos += headerFont.Size + padAfterHeader;

            foreach ((string EkTeam, string OpTeam, bool IsHomeGame, bool Fuck, string ScoreOrTime) game in day.Value)
            {
                float xPos = offsetX;
                outputImage.Mutate(x =>
                {
                    // draw ek team name
                    x.DrawRoundedRectangleWithCenteredText(
                        ekRect.Offset(xPos, yPos - (ekRect.Height - boldFont.Size) / 2), Rgba32.ParseHex("#f05a5a"),
                        game.EkTeam, boldFont, brushWhite);
                    xPos += ekRect.Width;

                    xPos += padBetweenRects;

                    // draw vs or @ depending on home/away game
                    x.DrawRoundedRectangleWithCenteredText(
                        atRect.Offset(xPos, yPos - (atRect.Height - boldFont.Size) / 2),
                        Rgba32.ParseHex("#cbcbcb"), game.IsHomeGame ? "vs" : "@", boldFont, brushBlack);
                    xPos += atRect.Width;

                    xPos += padBetweenRects;

                    // draw opponent team name
                    x.DrawRoundedRectangleWithCenteredText(
                        opRect.Offset(xPos, yPos - (opRect.Height - standardFont.Size) / 2),
                        Rgba32.ParseHex("#f05a5a"), game.OpTeam, standardFont, brushWhite);
                    xPos += opRect.Width;

                    xPos += padBetweenRects;

                    // draw score or time of the game
                    x.DrawRoundedRectangleWithCenteredText(
                        timeRect.Offset(xPos, yPos - (timeRect.Height - boldFont.Size) / 2),
                        Rgba32.ParseHex("#cbcbcb"), game.ScoreOrTime, boldFont, brushBlack);
                    xPos += timeRect.Width;
                    
                    if (game.Fuck)
                    {
                        x.DrawLine(brushBlack, 10f, new PointF(offsetX + padRect, yPos + boldFont.Size / 2), new PointF(offsetX + ekRect.Width + atRect.Width + opRect.Width + timeRect.Width + 3 * padBetweenRects - padRect, yPos + boldFont.Size / 2));
                    }
                });
                yPos += ekRect.Height + padAfterGame;
            }

            yPos += padAfterHeader - padAfterGame;
        }

        // save the image to the stream so we can process it further if asked by the user
        MemoryStream ms = new();
        await outputImage.SaveAsync(ms);
        ms.Position = 0;
        Stream = ms;
    }

    private static Stream GetResource(string name)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            throw new Exception("Missing embedded resource");
        }
        
        return stream;
    }

    private static async Task<Image> LoadImageAsync(string name)
    {
        await using var stream = GetResource(name);
        return await Image.LoadAsync(stream);
    }
}
