using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenAICreateQuestionsFromParagraphs
{
    internal class ConsolePrintHelper
    {
        public static void PrintMessage(string message, string type)
        {
            switch (type)
            {
                case "question":
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(message);
                    Console.ResetColor();
                    break;
                case "answer":
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(message);
                    Console.WriteLine();
                    Console.ResetColor();
                    break;
                case "retry":
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(message);
                    Console.WriteLine();
                    Console.ResetColor();
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(message);
                    Console.ResetColor();
                    break;
            }
        }
    }
}
