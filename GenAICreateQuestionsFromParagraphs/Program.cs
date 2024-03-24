using System.Text.Json;
using System;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
            var semanticKernelBuilder = Kernel.CreateBuilder();
            // Logging will be written to the debug output window
            semanticKernelBuilder.Services.AddLogging(configure => configure.AddConsole());

            foreach (var dbPedia in dbPedias)
            {
                var sampleQuestion = new DbPediaSampleQuestion
                {
                    Id = dbPedia.Id,
                    Title = dbPedia.Title,
                    Text = dbPedia.Text,
                    SampleQuestion = "What is " + dbPedia.Title + "?"
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
