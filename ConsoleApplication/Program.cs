using System.Text.RegularExpressions;
using Azure;
using Cocona;
using Microsoft.Extensions.Azure;
using Azure.AI.Language.QuestionAnswering;
using Azure.AI.Translation.Text;
using Azure.AI.Translation.Text.Custom;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

var builder = CoconaApp.CreateBuilder();

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddQuestionAnsweringClient(builder.Configuration.GetSection("QuestionAnswering"));
    clientBuilder.AddTextTranslationClient(builder.Configuration.GetSection("TextTranslation"));
});

var app = builder.Build();
app.Lifetime.ApplicationStopping.Register(
    _ =>
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[red]Stopping session[/]");
    },
    null
);

app.Lifetime.ApplicationStopped.Register(
    _ =>
    {
        AnsiConsole.WriteLine("Session stopped");
    },
    null
);

await app.RunAsync(
    async (
        CoconaAppContext ctx,
        IConfiguration configuration,
        QuestionAnsweringClient questionAnsweringClient,
        TextTranslationClient translationClient
    ) =>
    {
        var projectName = configuration["QuestionAnswering:ProjectName"];
        var deploymentName = configuration["QuestionAnswering:DeploymentName"];
        var project = new QuestionAnsweringProject(projectName, deploymentName);
        
        AnsiConsole.Write(new FigletText("BLOODBORNE FAQ").Color(Color.Red));

        Console.WriteLine("Press Ctrl+C to shutdown the application.");

        var languageCode = "en";

        if (!AnsiConsole.Confirm("Use default language? (English)"))
        {
            Response<GetLanguagesResult> languageResponse = await translationClient
                .GetLanguagesAsync(scope: "translation")
                .ConfigureAwait(false);
            GetLanguagesResult languages = languageResponse.Value;

            var language = AnsiConsole.Prompt(
                new SelectionPrompt<KeyValuePair<string, TranslationLanguage>>()
                    .Title("Choose a Language:")
                    .AddChoices(languages.Translation)
                    .UseConverter(l => l.Value.Name)
            );

            languageCode = language.Key;
        }

        while (!ctx.CancellationToken.IsCancellationRequested)
        {
            var question = AnsiConsole.Ask<string>("Q:");
            AnsiConsole.WriteLine();

            if (languageCode != "en")
            {
                string from = languageCode;
                string targetLanguage = "en";

                Response<IReadOnlyList<TranslatedTextItem>> translationResponse =
                    await translationClient
                        .TranslateAsync(targetLanguage, question, sourceLanguage: from)
                        .ConfigureAwait(false);
                IReadOnlyList<TranslatedTextItem> translations = translationResponse.Value;
                question = translations.FirstOrDefault()?.Translations.FirstOrDefault()?.Text;
            }

            var response = await questionAnsweringClient.GetAnswersAsync(
                question,
                project,
                cancellationToken: ctx.CancellationToken
            );

            foreach (var answer in response.Value.Answers)
            {
                var answerText = answer.Answer;

                if (languageCode != "en")
                {
                    string from = "en";
                    string targetLanguage = languageCode;

                    Response<IReadOnlyList<TranslatedTextItem>> translationResponse =
                        await translationClient
                            .TranslateAsync(targetLanguage, answerText, sourceLanguage: from)
                            .ConfigureAwait(false);
                    IReadOnlyList<TranslatedTextItem> translations = translationResponse.Value;
                    answerText = translations.FirstOrDefault()?.Translations.FirstOrDefault()?.Text;
                }

                const string pattern = @"\[(.+?)\]";

                const string replacementFormat = "[yellow]$1[/]";
                var result = Regex.Replace(answerText, pattern, replacementFormat);

                AnsiConsole.MarkupLine($"A: [green]{result}[/]");
                AnsiConsole.WriteLine();
            }
        }
    }
);
