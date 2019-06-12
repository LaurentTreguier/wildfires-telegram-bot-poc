# Wildfires Telegram Bot

Telegram bot attempt.
It will download images from the NASA API, show them, and ask if the user can see wildfire damages.
After repeating this and bisecting through images since 01/01/2000, the bot will tell the date of the fist image where wildfire damages are visible.

## Building

The bot is built in C# using .NET Core; it has been tested against .NET Core 2.2.

## Usage

The bot responds to 3 inputs from users.
`Start` will tell it to start the bisecting process
`Yes` and `No` will then tell it whether wildfire damages are visible on an image or not.
The commands are case insensitive and trimmed to be somewhat lenient.

## Known issues

- Retrieving images from the NASA API takes dates instead of ids. Because of this, sometimes, during the end of the process, the same image will be shown a second time instead of showing the proper image.
- Images containing clouds are still to be eliminated from the search.

## TODO

- The bot currently sleeps for 3.5 weeks waiting for connections; an actual waiting system should be used for any real usage
- Telegram has a nifty [reply keyboard markup](https://core.telegram.org/bots/api#replykeyboardmarkup) API, which would be very well suited for yes/no questions
