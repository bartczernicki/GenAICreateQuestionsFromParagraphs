using System.Text.Json;
using System;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Humanizer.Configuration;

namespace GenAICreateQuestionsFromParagraphs
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Load DbPedias");
            var dbPedias = LoadDbPedias("dbpedias.json");
            
            var dbPediaSampleQuestions = new List<DbPediaSampleQuestion>(100);

            // Set up SK
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();
            var azureOpenAIAPIKey = configuration.GetSection("AzureOpenAI")["APIKey"];
            var azureOpenAIEndpoint = configuration.GetSection("AzureOpenAI")["Endpoint"];
            var modelDeployment = "gpt-4-0125-preview";

            var semanticKernelBuilder = Kernel.CreateBuilder();
            // Logging will be written to the debug output window
            semanticKernelBuilder.Services.AddLogging(configure => configure.AddConsole());

            semanticKernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: modelDeployment,
                endpoint: azureOpenAIEndpoint!,
                apiKey: azureOpenAIAPIKey!
            );
            var semanticKernel = semanticKernelBuilder.Build();

            var pluginsDirectory = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "SemanticKernelPlugins", "QuestionPlugin");
            var createQuestionPlugin = semanticKernel.CreatePluginFromPromptDirectory(pluginsDirectory);

            foreach (var dbPedia in dbPedias)
            {
                var kernelFunction = createQuestionPlugin["CreateQuestion"];
                var promptsDictionary = new Dictionary<string, object>
                {
                    { "TITLE", dbPedia.Title },
                    { "PARAGRAPH", dbPedia.Text }
                };

                var kernelArguments = new KernelArguments(promptsDictionary);

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

        static List<DbPedia> LoadDbPedias(string filePath)
        {
            using (StreamReader r = new StreamReader(filePath))
            {
                string json = r.ReadToEnd();
                var dbpedias = JsonSerializer.Deserialize<List<DbPedia>>(json);
                return dbpedias;
            }
        }
    }
}
