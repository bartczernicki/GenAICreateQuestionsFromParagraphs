using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenAICreateQuestionsFromParagraphs
{
    internal class DbPediaSampleQuestion
    {
        public required string Id { get; set; }
        public required string Title { get; set; }
        public required string Text { get; set; }
        public required string SampleQuestion { get; set; }
    }
}
