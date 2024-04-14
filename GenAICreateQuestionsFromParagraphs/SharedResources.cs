using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenAICreateQuestionsFromParagraphs
{
    public static class SharedResources
    {
        public static ConcurrentBag<int> NumberOfHttpRetries = new ConcurrentBag<int>();
    }
}
