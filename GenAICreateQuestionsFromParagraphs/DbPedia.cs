
namespace GenAICreateQuestionsFromParagraphs
{
    internal class DbPedia
    {
        public required string Id { get; set; }
        public required string Title { get; set; }
        public required string Text { get; set; }
        public required List<float> Embeddings { get; set; }
        public required int TokenCount { get; set; } // Uses Title + Text
    }
}
