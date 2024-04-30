using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Serilog;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.Exporter;
using System.Diagnostics;
using OpenTelemetry.Instrumentation.Http;
using Microsoft.Extensions.Azure;

namespace GenAICreateQuestionsFromParagraphs
{
    internal class Program
    {
        /*
        Examples provided: https://github.com/microsoft/semantic-kernel/tree/main/dotnet/samples/TelemetryExample
        Http Instrumentation: https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Instrumentation.Http/README.md
        Activity Docs .NET: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs 
        SK Raw Prompt: https://github.com/microsoft/semantic-kernel/discussions/1239 
        Going forward (Semantic Conventions): https://github.com/open-telemetry/semantic-conventions/tree/main/docs/gen-ai 
        TimoutAsync: https://learn.microsoft.com/en-us/answers/questions/1419955/how-to-implement-polly-retry-and-fallback
        Logger (context) to Policies: https://www.stevejgordon.co.uk/passing-an-ilogger-to-polly-policies 
        */

        private const string DBPEDIASQUESTIONSDIRECTORY = @"DbPediaQuestions";
        private const int NUMBEROFQUESTIONSTOPROCESS = 100;

        private const LogLevel MinLogLevel = LogLevel.Trace;
        private static readonly ActivitySource s_activitySource = new("Custom.Telemetry");

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

            // Set up SK
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();

            // Retrieve the keys
            var azureOpenAIAPIKey = configuration.GetSection("AzureOpenAI")["APIKey"];
            var azureOpenAIEndpoint = configuration.GetSection("AzureOpenAI")["Endpoint"];
            var modelDeploymentName = configuration.GetSection("AzureOpenAI")["ModelDeploymentName"];
            var azureOpenAIType = configuration.GetSection("AzureOpenAI")["AzureOpenAIType"];
            var appInsightsConnectionString = configuration.GetSection("ApplicationInsights")["ConnectionString"];

            // OpenTelemetry: Traces to provide request lifecycle information
            using var traceProvider = Sdk.CreateTracerProviderBuilder()
                .AddHttpClientInstrumentation()
                .AddSource("Microsoft.SemanticKernel*")
                .AddSource("Custom.Telemetry")
                .AddAzureMonitorTraceExporter(options => options.ConnectionString = appInsightsConnectionString)
                .Build();

            // OpenTelemetry: Meters to provide aggregate metrics (statistics)
            using var meterProvider = Sdk.CreateMeterProviderBuilder()
                .AddHttpClientInstrumentation()
                .AddMeter("Microsoft.SemanticKernel*")
                .AddAzureMonitorMetricExporter(options => options.ConnectionString = appInsightsConnectionString)
                .Build();

            // OpenTelemetry: Logging to provide log information
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                // Add OpenTelemetry as a logging provider
                builder.AddOpenTelemetry(options =>
                {
                    options.AddAzureMonitorLogExporter(options => options.ConnectionString = appInsightsConnectionString);
                    // Format log messages. This is default to false.
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                });
                builder.SetMinimumLevel(MinLogLevel);
            });

            //// Use Seri Log for logging and Sinks (files)
            //var seriLoggerSemanticKernel = new LoggerConfiguration()
            //    .Enrich.WithThreadId()
            //    .MinimumLevel.Verbose()
            //    .WriteTo.File("SeriLog-SemanticKernel.log")
            //    .CreateLogger();

            //var seriLoggerSemanticKernelHttpClient = new LoggerConfiguration()
            //    .Enrich.WithThreadId()
            //    .MinimumLevel.Verbose()
            //    // Can be tuned: https://stackoverflow.com/questions/70609720/reducing-httpclient-log-verbosity
            //    .WriteTo.File("SeriLog-SemanticKernelHttpClient.log")
            //    .CreateLogger();


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
                // Retrieve Polly retry policy and apply it to all the services making web requests
                var retryPolicy = Policies.HttpPolicies.GetRetryPolicy();

                    // Apply the Polly policy to both the OpenAI and the Project Gutenberg services
                    services.AddHttpClient("DefaultSemanticKernelService", configureClient =>
                    {
                        configureClient.Timeout = TimeSpan.FromSeconds(200);
                    }
                    )
                    .AddPolicyHandler(retryPolicy);

                    services.AddScoped<ILoggerFactory>(_ => loggerFactory);
                });
            builder.ConfigureLogging(cfg => cfg.ClearProviders()
            // add OpenTelemetry logging provider
            .AddOpenTelemetry(options => options.IncludeFormattedMessage = true)
            );
               // .AddSerilog(seriLoggerSemanticKernelHttpClient));

            var host = builder.Build();

            // Semantic Kernel Builder
            var semanticKernelBuilder = Kernel.CreateBuilder();
            // Semantic Kernel Builder - Add Logging Factory -> App Insights
            semanticKernelBuilder.Services.AddSingleton<ILoggerFactory>(loggerFactory);

            // Logging will be written to the debug output window
            //semanticKernelBuilder.Services.AddLogging(configure => configure
            //    .SetMinimumLevel(LogLevel.Trace)
            //    .AddSerilog(seriLoggerSemanticKernel)
            //   );

            // Semantic Kernel Builder - Add custom HttpClient with Retry Policy (dynamically added based on selected processing)
            var httpClientForSemanticKernel = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("DefaultSemanticKernelService");

            semanticKernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: modelDeploymentName!,
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
                // Load the DBPedia Questions from the dbPediasSampleQuestions.json file
                var dbPediaSampleQuestions = LoadDbPediaQuestions(Path.Combine(DBPEDIASQUESTIONSDIRECTORY, "dbPediasSampleQuestions.json"));
                dbPediaSampleQuestions = dbPediaSampleQuestions.Take(NUMBEROFQUESTIONSTOPROCESS).ToList();
                // Populate a ConcurrentQueue (for later flexibility)
                var dbPediaSampleQuestionsQueue = new ConcurrentQueue<DbPediaSampleQuestion>(dbPediaSampleQuestions);
                var durationResults = new List<double>(dbPediaSampleQuestionsQueue.Count);

                var pluginsDirectory = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "SemanticKernelPlugins", "QuestionPlugin");
                var createQuestionPlugin = semanticKernel.CreatePluginFromPromptDirectory(pluginsDirectory);

                // Do this in parallel to saturate the Azure OpenAI Endpoint
                object sync = new object();
                var currentTime = DateTime.UtcNow;
                Parallel.ForEach(dbPediaSampleQuestionsQueue, dbPediaQuestion =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    ConsolePrintHelper.PrintMessage($"Generating ANSWER for ID: {dbPediaQuestion.Id} - {dbPediaQuestion.SampleQuestion}", "question");

                    var kernelFunction = createQuestionPlugin["AnswerQuestion"];

                    var promptsDictionary = new Dictionary<string, object>
                    {
                        { "TITLE", dbPediaQuestion.Title },
                        { "PARAGRAPH", dbPediaQuestion.Text },
                        { "QUESTION", dbPediaQuestion.SampleQuestion }
                    };

                    var kernelArguments = new KernelArguments(promptsDictionary!);

                    using (var activity = s_activitySource.StartActivity($"AnswerRAGQuestion - ModelDeployment: {modelDeploymentName}"))
                    {
                        var generatedQuestion = semanticKernel.InvokeAsync(kernelFunction, kernelArguments).Result;

                        lock (sync)
                        {
                            var dateTimeOffSet = (DateTimeOffset)generatedQuestion!.Metadata["Created"];
                            // retrieve the Semantic Kernel function duration
                            var diff = (DateTime.UtcNow - dateTimeOffSet.UtcDateTime).TotalSeconds;
                            durationResults.Add(diff);
                        };

                        var generatedQuestionString = generatedQuestion.GetValue<string>() ?? string.Empty;
                        ConsolePrintHelper.PrintMessage($"ANSWER for ID: {dbPediaQuestion.Id} - {generatedQuestionString}", "answer");
                    }
                });
                var currentTimeAfterRun = DateTime.UtcNow;
                var totalDurationWithRetries = (currentTimeAfterRun - currentTime).TotalSeconds;

                // Write out the duration results into a string
                var sb = new StringBuilder();
                foreach (var duration in durationResults)
                {
                    sb.AppendLine(duration.ToString());
                }

                File.WriteAllText($"sampleDurationResults-{NUMBEROFQUESTIONSTOPROCESS}-{azureOpenAIType}-{modelDeploymentName}.txt", sb.ToString());

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Finished Processing {NUMBEROFQUESTIONSTOPROCESS} questions using {azureOpenAIType}:{modelDeploymentName}");
                Console.WriteLine($"Total   Processing Time - Sum of {NUMBEROFQUESTIONSTOPROCESS} requests  (sec): {durationResults.Sum()}");
                Console.WriteLine($"Average Processing Time - Avg of {NUMBEROFQUESTIONSTOPROCESS} requests  (sec): {durationResults.Average()}");
                Console.WriteLine($"Total   Processing Time - Time to process in parallel (logic & retries) (sec): {totalDurationWithRetries}");
                Console.WriteLine($"Total   Number of HttpClient Retries: {SharedResources.NumberOfHttpRetries.Count()}");
                Console.WriteLine();
            }
        }

        static List<DbPedia> LoadDbPedias(string filePath)
        {
            using (StreamReader r = new StreamReader(filePath))
            {
                string json = r.ReadToEnd();
                var dbpedias = JsonSerializer.Deserialize<List<DbPedia>>(json);
                return dbpedias!;
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
