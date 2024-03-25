﻿using System.Text.Json;
using System;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Humanizer.Configuration;
using Microsoft.Extensions.Hosting;
using System.Runtime.CompilerServices;
using System.Text;
using Azure.AI.OpenAI;

namespace GenAICreateQuestionsFromParagraphs
{
    internal class Program
    {
        private const string DBPEDIASQUESTIONSDIRECTORY = @"DbPediaQuestions";
        private const int NUMBEROFQUESTIONSTOPROCESS = 100;

        static async Task Main(string[] args)
        {
            Console.Title = "GenAI - Create & Answer Questions From DbPedia";

            var asciiArt = 
                """
                ,-----.                    ,--.             ,---.          ,---.                                         
                '  .--.,--.--.,---. ,--,--,-'  '-.,---.     |  o ,-.       /  O  \,--,--, ,---.,--.   ,--.,---.,--.--.    
                |  |   |  .--| .-. ' ,-.  '-.  .-| .-. :    .'     /_     |  .-.  |      (  .-'|  |.'.|  | .-. |  .--'    
                '  '--'|  |  \   --\ '-'  | |  | \   --.    |  o  .__)    |  | |  |  ||  .-'  `|   .'.   \   --|  |       
                ,-----.--'   `----'`--`--' `-,--.`,--.'     `---'        `--' `--`--''--`----''--'   '--'`----`--'       
                '  .-.  ' ,--.,--.,---. ,---,-'  '-`--',---.,--,--, ,---.                                                 
                |  | |  | |  ||  | .-. (  .-'-.  .-,--| .-. |      (  .-'                                                 
                '  '-'  '-'  ''  \   --.-'  `)|  | |  ' '-' |  ||  .-'  `)                                                
                `-----'--'`----' `----`----' `--' `--'`---'`--''--`----'   
                """;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(asciiArt);
            ProcessingOptions selectedProcessingChoice = (ProcessingOptions)0;
            bool validInput = false;

            // Iterate until the proper input is selected by the user
            while (!validInput)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(string.Empty);
                Console.WriteLine("Select one of the options, by typing either 1 through 3:");
                Console.WriteLine("1) Create questions from DbPedia titles and text");
                Console.WriteLine("2) Answer questions generated by GenAI");
                Console.WriteLine("3) Answer questions generated by GenAI (at scale)");

                var insertedText = Console.ReadLine();
                string trimmedInput = insertedText!.Trim();

                if (trimmedInput == "1" || trimmedInput == "2" || trimmedInput == "3")
                {
                    validInput = true;
                    selectedProcessingChoice = (ProcessingOptions) Int32.Parse(trimmedInput);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Incorrect selection!!!!");
                }
            }
            Console.WriteLine("You selected: {0}", selectedProcessingChoice);

            var builder = new HostBuilder();
            builder
                .ConfigureServices((hostContext, services) =>
                {
                    // Add AddHttpClient we register the IHttpClientFactory
                    services.AddHttpClient();

                    // Retrieve Polly retry policy and apply it to all the services making web requests
                    var retryPolicy = Policies.HttpPolicies.GetRetryPolicy();

                    // Apply the Polly policy to both the OpenAI and the Project Gutenberg services
                    services.AddHttpClient("DefaultSemanticKernelService", configureClient =>
                    {
                        configureClient.Timeout = TimeSpan.FromSeconds(200);
                    }
                    ).AddPolicyHandler(retryPolicy);
                });
            var host = builder.Build();

            // Set up SK
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();

            // PAYGO Azure OpenAI
            var azureOpenAIAPIKey = configuration.GetSection("AzureOpenAI")["APIKey"];
            var azureOpenAIEndpoint = configuration.GetSection("AzureOpenAI")["Endpoint"];
            var modelDeployment = "gpt-4-preview-1106";

            // PTU Azure OpenAI
            //var azureOpenAIAPIKey = configuration.GetSection("AzureOpenAI")["APIKeyPTU"];
            //var azureOpenAIEndpoint = configuration.GetSection("AzureOpenAI")["EndpointPTU"];
            //var modelDeployment = "gpt-4-1106-ptu";

            var semanticKernelBuilder = Kernel.CreateBuilder();
            // Logging will be written to the debug output window
            semanticKernelBuilder.Services.AddLogging(configure => configure.AddConsole());
            var httpClientForSemanticKernel = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("DefaultSemanticKernelService");

            semanticKernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: modelDeployment,
                endpoint: azureOpenAIEndpoint!,
                apiKey: azureOpenAIAPIKey!,
                httpClient: (selectedProcessingChoice == (ProcessingOptions.AnswerQuestionsAtScale)) ? httpClientForSemanticKernel : null
            );
            var semanticKernel = semanticKernelBuilder.Build();

            if (selectedProcessingChoice == (ProcessingOptions.CreateQuestions))
            {
                Console.WriteLine("Load DbPedias");
                var dbPedias = LoadDbPedias("dbpedias.json");
                var dbPediaSampleQuestions = new List<DbPediaSampleQuestion>(NUMBEROFQUESTIONSTOPROCESS);

                var pluginsDirectory = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "SemanticKernelPlugins", "QuestionPlugin");
                var createQuestionPlugin = semanticKernel.CreatePluginFromPromptDirectory(pluginsDirectory);

                foreach (var dbPedia in dbPedias)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Generating Question for {dbPedia.Title}");

                    var kernelFunction = createQuestionPlugin["CreateQuestion"];
                    var promptsDictionary = new Dictionary<string, object>
                    {
                        { "TITLE", dbPedia.Title },
                        { "PARAGRAPH", dbPedia.Text }
                    };

                    var kernelArguments = new KernelArguments(promptsDictionary!);

                    var generatedQuestion = await semanticKernel.InvokeAsync(kernelFunction, kernelArguments);

                    var sampleQuestion = new DbPediaSampleQuestion
                    {
                        Id = dbPedia.Id,
                        Title = dbPedia.Title,
                        Text = dbPedia.Text,
                        SampleQuestion = generatedQuestion.GetValue<string>() ?? string.Empty
                    };

                    dbPediaSampleQuestions.Add(sampleQuestion);
                }

                Console.WriteLine("Save DbPedias with Sample Questions");
                var dbPediaSampleQuestionsJson = JsonSerializer.Serialize(dbPediaSampleQuestions);
                File.WriteAllText(("dbPediasSampleQuestions.json"), dbPediaSampleQuestionsJson);
            }
            else if(
                (selectedProcessingChoice == ProcessingOptions.AnswerQuestions) || (selectedProcessingChoice == ProcessingOptions.AnswerQuestionsAtScale))
            {
                var dbPediaQuestions = LoadDbPediaQuestions(Path.Combine(DBPEDIASQUESTIONSDIRECTORY, "dbPediasSampleQuestions.json"));
                dbPediaQuestions = dbPediaQuestions.Take(NUMBEROFQUESTIONSTOPROCESS).ToList();
                var durationResults = new List<double>(dbPediaQuestions.Count);

                var pluginsDirectory = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "SemanticKernelPlugins", "QuestionPlugin");
                var createQuestionPlugin = semanticKernel.CreatePluginFromPromptDirectory(pluginsDirectory);

                // Do this in parallel to saturate the Azure OpenAI Endpoint
                object sync = new object();
                var currentTime = DateTime.UtcNow;
                Parallel.ForEach(dbPediaQuestions, dbPediaQuestion =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Generating ANSWER for {dbPediaQuestion.SampleQuestion}");

                    var kernelFunction = createQuestionPlugin["AnswerQuestion"];

                    var promptsDictionary = new Dictionary<string, object>
                    {
                        { "TITLE", dbPediaQuestion.Title },
                        { "PARAGRAPH", dbPediaQuestion.Text },
                        { "QUESTION", dbPediaQuestion.SampleQuestion }
                    };

                    var kernelArguments = new KernelArguments(promptsDictionary!);

                    var generatedQuestion = semanticKernel.InvokeAsync(kernelFunction, kernelArguments).Result;

                    lock (sync)
                    {
                        var dateTimeOffSet = (DateTimeOffset) generatedQuestion?.Metadata["Created"];
                        // retrieve the Semantic Kernel function duration
                        var diff = (DateTime.UtcNow - dateTimeOffSet.UtcDateTime).TotalSeconds;
                        durationResults.Add(diff);
                    };

                    var generatedQuestionString = generatedQuestion.GetValue<string>() ?? string.Empty;
                    Console.WriteLine($"ANSWER: {generatedQuestionString}");
                    Console.WriteLine();
                });
                var currentTimeAfterRun = DateTime.UtcNow;
                var totalDurationWithRetries = (currentTimeAfterRun - currentTime).TotalSeconds;

                // Write out the duration results into a string
                var sb = new StringBuilder();
                foreach (var duration in durationResults)
                {
                    sb.AppendLine(duration.ToString());
                }
                File.WriteAllText("sampleDurationResults.txt", sb.ToString());

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Finished Processing {dbPediaQuestions.Count} questions.");
                Console.WriteLine($"Total   Processing Time - Sum of 100 requests  (sec): {durationResults.Sum()}");
                Console.WriteLine($"Average Processing Time - Avg of 100 requests  (sec): {durationResults.Average()}");
                Console.WriteLine($"Total   Processing Time - With logic & retries (sec): {totalDurationWithRetries}");
                Console.WriteLine();
            }
        }

        static List<DbPedia> LoadDbPedias(string filePath)
        {
            using (StreamReader r = new StreamReader(filePath))
            {
                string json = r.ReadToEnd();
                var dbpedias = JsonSerializer.Deserialize<List<DbPedia>>(json);
                return dbpedias;
            }
        }

        static List<DbPediaSampleQuestion> LoadDbPediaQuestions(string filePath)
        {
            using (StreamReader r = new StreamReader(filePath))
            {
                string json = r.ReadToEnd();
                var dbpedias = JsonSerializer.Deserialize<List<DbPediaSampleQuestion>>(json);
                return dbpedias;
            }
        }
    }
}
