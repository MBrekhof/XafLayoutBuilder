namespace XafLayoutBuilder.Tests;

// Mirrors the sample module's Order without pulling DevExpress into the Core tests.
public class TestOrder {
    public string Number { get; set; } = "";
    public TestCustomer? Customer { get; set; }
    public DateTime OrderDate { get; set; }
    public string? Notes { get; set; }
    public string? SyncToken { get; set; }
    public IList<object> Lines { get; set; } = [];
    public IList<object> Attachments { get; set; } = [];
}

public class TestCustomer {
    public string Name { get; set; } = "";
}
