using System;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.SemanticKernel.Embeddings;
using System.Collections.Generic;
using System.Linq;

// Program class
class Program
{
    // Main method
    static async Task Main(string[] args)
    {
        try
        {
            // Load the .env file
            Env.Load();

            // Load Azure OpenAI endpoint from config file
            string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

            // Load Model Deployment name from config file
            string model_chat_deployment_name = Environment.GetEnvironmentVariable("MODEL_CHAT_DEPLOYMENT_NAME");
            string model_embeddings_deployment_name = Environment.GetEnvironmentVariable("MODEL_EMBEDDINGS_DEPLOYMENT_NAME");

            // Load Model Id from config file
            string model_chat_id = Environment.GetEnvironmentVariable("MODEL_CHAT_ID");
            string model_embeddings_id = Environment.GetEnvironmentVariable("MODEL_EMBEDDINGS_ID");

            // Specify the model version
            string model_chat_version = Environment.GetEnvironmentVariable("MODEL_CHAT_VERSION");
            string model_embeddings_version = Environment.GetEnvironmentVariable("MODEL_EMBEDDINGS_VERSION");

            var credentials = new DefaultAzureCredential();

            // Create a new Chat Completion Service
            AzureOpenAIChatCompletionService chatService = new(
                deploymentName: model_chat_deployment_name,
                endpoint: endpoint,
                credentials: credentials,
                modelId: model_chat_id,
                apiVersion: model_chat_version);

            // Send a message
            var chatHistory = new ChatHistory("You are a comedian, expert about being funny");
            chatHistory.AddUserMessage("Tell me a joke.");
            // Get the reply
            var reply = await chatService.GetChatMessageContentAsync(chatHistory);

            // Get message details
            var replyInnerContent = reply.InnerContent as OpenAI.Chat.ChatCompletion;

            // Output message details
            Program.OutputInnerContent(replyInnerContent!);

            // Create an embedding generation service.
            var textEmbeddingGenerationService = new AzureOpenAITextEmbeddingGenerationService(
                    model_embeddings_deployment_name,
                    endpoint,
                    new AzureCliCredential(),
                    apiVersion: model_embeddings_version);

            // Construct an InMemory vector store.
            var vectorStore = new InMemoryVectorStore();

            // Get and create collection if it doesn't exist.
            var collection = vectorStore.GetCollection<ulong, Glossary>("skglossary");
            await collection.CreateCollectionIfNotExistsAsync();

            // Create glossary entries and generate embeddings for them.
            var glossaryEntries = CreateGlossaryEntries().ToList();
            var tasks = glossaryEntries.Select(entry => Task.Run(async () =>
            {
                entry.DefinitionEmbedding = new ReadOnlyMemory<float>((await textEmbeddingGenerationService.GenerateEmbeddingAsync(entry.Definition).ConfigureAwait(false)).ToArray());
            }));
            await Task.WhenAll(tasks);

            // Upsert the glossary entries into the collection and return their keys.
            var upsertedKeysTasks = glossaryEntries.Select(x => collection.UpsertAsync(x));
            var upsertedKeys = await Task.WhenAll(upsertedKeysTasks);

            // Search the collection using a vector search.
            var searchString = "What is an Application Programming Interface";
            var searchVector = await textEmbeddingGenerationService.GenerateEmbeddingAsync(searchString);
            var searchResult = await collection.VectorizedSearchAsync(searchVector, new() { Top = 1 });
            var resultRecords = new List<VectorSearchResult<Glossary>>();
            await foreach (var result in searchResult.Results)
            {
                resultRecords.Add(result);
            }

            Console.WriteLine("Search string: " + searchString);
            Console.WriteLine("Result: " + resultRecords.First().Record.Definition);
            Console.WriteLine();

            // Search the collection using a vector search.
            searchString = "What is Retrieval Augmented Generation";
            searchVector = await textEmbeddingGenerationService.GenerateEmbeddingAsync(searchString);
            searchResult = await collection.VectorizedSearchAsync(searchVector, new() { Top = 1 });
            resultRecords = new List<VectorSearchResult<Glossary>>();
            await foreach (var result in searchResult.Results)
            {
                resultRecords.Add(result);
            }

            Console.WriteLine("Search string: " + searchString);
            Console.WriteLine("Result: " + resultRecords.First().Record.Definition);
            Console.WriteLine();

            // Search the collection using a vector search with pre-filtering.
            searchString = "What is Retrieval Augmented Generation";
            searchVector = await textEmbeddingGenerationService.GenerateEmbeddingAsync(searchString);
            var filter = new VectorSearchFilter().EqualTo(nameof(Glossary.Category), "External Definitions");
            searchResult = await collection.VectorizedSearchAsync(searchVector, new() { Top = 3, Filter = filter });
            resultRecords = new List<VectorSearchResult<Glossary>>();
            await foreach (var result in searchResult.Results)
            {
                resultRecords.Add(result);
            }

            Console.WriteLine("Search string: " + searchString);
            Console.WriteLine("Number of results: " + resultRecords.Count);
            Console.WriteLine("Result 1 Score: " + resultRecords[0].Score);
            Console.WriteLine("Result 1: " + resultRecords[0].Record.Definition);
            Console.WriteLine("Result 2 Score: " + resultRecords[1].Score);
            Console.WriteLine("Result 2: " + resultRecords[1].Record.Definition);
        }
        catch (System.NotSupportedException ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }


    /// <summary>
    /// Sample model class that represents a glossary entry.
    /// </summary>
    /// <remarks>
    /// Note that each property is decorated with an attribute that specifies how the property should be treated by the vector store.
    /// This allows us to create a collection in the vector store and upsert and retrieve instances of this class without any further configuration.
    /// </remarks>
    private sealed class Glossary
    {
        [VectorStoreRecordKey]
        public ulong Key { get; set; }

        [VectorStoreRecordData(IsFilterable = true)]
        public string Category { get; set; }

        [VectorStoreRecordData]
        public string Term { get; set; }

        [VectorStoreRecordData]
        public string Definition { get; set; }

        [VectorStoreRecordVector(1536)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }

    /// <summary>
    /// Create some sample glossary entries.
    /// </summary>
    /// <returns>A list of sample glossary entries.</returns>
    private static IEnumerable<Glossary> CreateGlossaryEntries()
    {
        yield return new Glossary
        {
            Key = 1,
            Category = "External Definitions",
            Term = "API",
            Definition = "Application Programming Interface. A set of rules and specifications that allow software components to communicate and exchange data."
        };

        yield return new Glossary
        {
            Key = 2,
            Category = "Core Definitions",
            Term = "Connectors",
            Definition = "Connectors allow you to integrate with various services provide AI capabilities, including LLM, AudioToText, TextToAudio, Embedding generation, etc."
        };

        yield return new Glossary
        {
            Key = 3,
            Category = "External Definitions",
            Term = "RAG",
            Definition = "Retrieval Augmented Generation - a term that refers to the process of retrieving additional data to provide as context to an LLM to use when generating a response (completion) to a user’s question (prompt)."
        };
    }

    /// <summary>
    /// Retrieve extra information from a <see cref="ChatMessageContent"/> inner content of type <see cref="OpenAI.Chat.ChatCompletion"/>.
    /// </summary>
    /// <param name="innerContent">An instance of <see cref="OpenAI.Chat.ChatCompletion"/> retrieved as an inner content of <see cref="ChatMessageContent"/>.</param>
    /// <remarks>
    /// This is a breaking glass scenario, any attempt on running with different versions of OpenAI SDK that introduces breaking changes
    /// may break the code below.
    /// </remarks>
    static void OutputInnerContent(OpenAI.Chat.ChatCompletion innerContent)
    {
        Console.WriteLine($"=================================");
        Console.WriteLine($"Message role: {innerContent.Role}"); // Available as a property of ChatMessageContent
        Console.WriteLine($"Message content: {innerContent.Content[0].Text}"); // Available as a property of ChatMessageContent

        Console.WriteLine($"Model: {innerContent.Model}"); // Model doesn't change per chunk, so we can get it from the first chunk only
        Console.WriteLine($"Created At: {innerContent.CreatedAt}");

        Console.WriteLine($"Finish reason: {innerContent.FinishReason}");
        Console.WriteLine($"Input tokens usage: {innerContent.Usage.InputTokenCount}");
        Console.WriteLine($"Output tokens usage: {innerContent.Usage.OutputTokenCount}");
        Console.WriteLine($"Total tokens usage: {innerContent.Usage.TotalTokenCount}");
        Console.WriteLine($"Refusal: {innerContent.Refusal} ");
        Console.WriteLine($"Id: {innerContent.Id}");
        Console.WriteLine($"System fingerprint: {innerContent.SystemFingerprint}");

        if (innerContent.ContentTokenLogProbabilities.Count > 0)
        {
            Console.WriteLine("Content token log probabilities:");
            foreach (var contentTokenLogProbability in innerContent.ContentTokenLogProbabilities)
            {
                Console.WriteLine($"Token: {contentTokenLogProbability.Token}");
                Console.WriteLine($"Log probability: {contentTokenLogProbability.LogProbability}");

                Console.WriteLine("   Top log probabilities for this token:");
                foreach (var topLogProbability in contentTokenLogProbability.TopLogProbabilities)
                {
                    Console.WriteLine($"   Token: {topLogProbability.Token}");
                    Console.WriteLine($"   Log probability: {topLogProbability.LogProbability}");
                    Console.WriteLine("   =======");
                }
                Console.WriteLine("--------------");
            }
        }

        if (innerContent.RefusalTokenLogProbabilities.Count > 0)
        {
            Console.WriteLine("Refusal token log probabilities:");
            foreach (var refusalTokenLogProbability in innerContent.RefusalTokenLogProbabilities)
            {
                Console.WriteLine($"Token: {refusalTokenLogProbability.Token}");
                Console.WriteLine($"Log probability: {refusalTokenLogProbability.LogProbability}");

                Console.WriteLine("   Refusal top log probabilities for this token:");
                foreach (var topLogProbability in refusalTokenLogProbability.TopLogProbabilities)
                {
                    Console.WriteLine($"   Token: {topLogProbability.Token}");
                    Console.WriteLine($"   Log probability: {topLogProbability.LogProbability}");
                    Console.WriteLine("   =======");
                }
            }
        }
    }


}