namespace IntegrationAssignment.Configuration;

public sealed class QueueOptions
{
    public string ProductReceived { get; set; } = "q.hf.int001.products.dev.received";

    public string ProductValid { get; set; } = "q.hf.int001.products.dev.valid";

    public string ProductInvalid { get; set; } = "q.hf.int001.products.dev.invalid";
}
