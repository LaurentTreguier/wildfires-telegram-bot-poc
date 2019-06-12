using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Serialization;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;

namespace Wildfires
{
    class Constants
    {
        // Will be filled at startup from command line arguments
        public static string NASA_API_KEY;
        // float.ToString() will output a string with commas, but we have to feed the API numbers using dots instead
        public static string LONGITUDE = (-120.70418).ToString().Replace(',', '.');
        public static string LATITUDE = 38.32974.ToString().Replace(',', '.');
        public const string BEGIN = "2000-01-01";
        public const string EARTH_ENDPOINT = "https://api.nasa.gov/planetary/earth/";
        // The rest client should use a custom JSON serializer (https://github.com/restsharp/RestSharp#note-on-json-serialization)
        public static IRestClient Rest = new RestClient(EARTH_ENDPOINT).UseSerializer<JsonNetSerializer>();
    }

    class Program
    {
        /// Bot instance used for communicating with users
        public static TelegramBotClient bot;
        /// Association of bisectors with user chat ids to keep track of multiple conversations
        private static Dictionary<long, Bisector> bisectors = new Dictionary<long, Bisector>();

        /// Entry point; makes the NASA API key available to the rest of the program, and initializes the bot
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("You must supply the Telegram API key and the NASA API key on the command line, in that order");
                return;
            }

            Constants.NASA_API_KEY = args[1];
            bot = new TelegramBotClient(args[0]);
            bot.OnMessage += OnMessageAsync;
            bot.StartReceiving();
            // TODO: provide an elegant way to keep the bot alive
            Thread.Sleep(Timeout.Infinite);
        }

        /// Safely tries to retrieve a bisector associated with the chat, if any
        private static Bisector GetBisector(ChatId chat) =>
            bisectors.ContainsKey(chat.Identifier) ? bisectors[chat.Identifier] : null;

        /// Processes incoming user messages
        private static async void OnMessageAsync(object sender, MessageEventArgs e)
        {
            if (e.Message.Text == null)
            {
                return;
            }

            var bisector = GetBisector(e.Message.Chat);

            switch (e.Message.Text.Trim().ToUpper())
            {
                case "START":
                    await StartBisectingAsync(e.Message.Chat);
                    bisector = GetBisector(e.Message.Chat);
                    break;
                case "YES":
                    bisector?.Bisect(true);
                    break;
                case "NO":
                    bisector?.Bisect(false);
                    break;
                default: return;
            }

            /// If we have no bisector by now, this means that the message was not "START", "YES", or "NO"
            if (bisector == null)
            {
                return;
            }

            if (bisector.Completed)
            {
                await bot.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: "Culprit: " + bisector.Culprit
                );
                bisectors.Remove(e.Message.Chat.Id);
            }
            else
            {
                await ContinueBisectingAsync(e.Message.Chat);
            }
        }

        private static async Task StartBisectingAsync(ChatId chat)
        {
            var request = new RestRequest("assets", Method.GET)
                .AddParameter("api_key", Constants.NASA_API_KEY)
                .AddParameter("lon", Constants.LONGITUDE)
                .AddParameter("lat", Constants.LATITUDE)
                .AddParameter("begin", Constants.BEGIN);
            var imageList = await Constants.Rest.GetAsync<ImageList>(request);
            var images = imageList.results;

            if (images.Length == 0)
            {
                await bot.SendTextMessageAsync(
                    chatId: chat,
                    text: "Cannot bisect an empty array..."
                );
            }
            else
            {
                bisectors[chat.Identifier] = new Bisector(images);
            }
        }

        private static async Task ContinueBisectingAsync(ChatId chat)
        {
            var info = await bisectors[chat.Identifier].GetMiddleImageInfoAsync();
            await bot.SendPhotoAsync(
                chatId: chat,
                photo: new InputOnlineFile(info.url),
                caption: $"[{info.date}] - Do you see wildfire damages ?"
            );
        }
    }

    /// Bisects a range of images.
    /// Each call to Bisect() will cut down their numbers, until only a single one remains.
    class Bisector
    {
        /// The range of images to bisect
        private ImageIdentifier[] images;
        /// The date of the first image to show wildfire damages
        public string Culprit { get; private set; }
        /// Whether the bisection is complete
        public bool Completed { get; private set; }

        public Bisector(ImageIdentifier[] images)
        {
            this.images = images;
            Array.Sort(this.images, Comparer<ImageIdentifier>.Create(
                (a, b) => DateTime.Parse(a.date).CompareTo(DateTime.Parse(b.date))
            ));
        }

        /// Cut the number of potnetial culprit images by half.
        /// If a fire has been spotted in the middle image, the first half will be kept;
        /// otherwise the second half will be.
        public void Bisect(bool fireSeen)
        {
            var index = GetMiddleIndex();

            if (fireSeen)
            {
                Culprit = images[index].date;
            }

            if (images.Length <= 1)
            {
                Completed = true;
            }
            else
            {
                images = (fireSeen ? images.Take(index) : images.TakeLast(images.Length - index - 1)).ToArray();
            }
        }

        /// Retrieves information about the image in the middle of the image list
        public async Task<ImageInfo> GetMiddleImageInfoAsync()
        {
            var index = GetMiddleIndex();
            var image = images[index];
            var date = DateTime.Parse(image.date);
            // Put trailing slash to avoid being redirected from HTTPS to HTTP, which is not accepted by .NET Core
            var request = new RestRequest("imagery/", Method.GET)
                .AddParameter("cloud_score", true)
                .AddParameter("api_key", Constants.NASA_API_KEY)
                .AddParameter("lon", Constants.LONGITUDE)
                .AddParameter("lat", Constants.LATITUDE)
                // Don't put the date as is, the API only accepts YYYY-MM-DD
                .AddParameter("date", $"{date.Year}-{date.Month}-{date.Day}");
            return await Constants.Rest.GetAsync<ImageInfo>(request);
        }

        private int GetMiddleIndex() => images.Length / 2;
    }

    #region API

    /// Result obtained from the NASA assets endpoint (https://api.nasa.gov/api.html#assets)
    class ImageList
    {
        public int count;
        public ImageIdentifier[] results;
    }

    class ImageIdentifier
    {
        public string date;
        public string id;
    }

    /// Result obtained from the NASA imagery endpoint (https://api.nasa.gov/api.html#imagery)
    class ImageInfo
    {
        public float? cloud_score;
        public string date;
        public string id;
        public ImageResource resource;
        public string service_version;
        public string url;
    }

    class ImageResource
    {
        public string dataset;
        public string planet;
    }

    /// JSON Serializer for RestSharp, taken from this snippet:
    /// https://gist.github.com/alexeyzimarev/c00b79c11c8cce6f6208454f7933ad24
    class JsonNetSerializer : IRestSerializer
    {
        public string Serialize(object obj) =>
            JsonConvert.SerializeObject(obj);

        public string Serialize(Parameter bodyParameter) =>
            JsonConvert.SerializeObject(bodyParameter.Value);

        public T Deserialize<T>(IRestResponse response) =>
            JsonConvert.DeserializeObject<T>(response.Content);

        public string[] SupportedContentTypes { get; } = {
            "application/json", "text/json", "text/x-json", "text/javascript", "*+json"
        };

        public string ContentType { get; set; } = "application/json";

        public DataFormat DataFormat { get; } = DataFormat.Json;
    }

    #endregion API
}
