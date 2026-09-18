namespace Prod_IssueTracker_POC.Web.Models
{
    /// <summary>One row in the reusable tag catalog (the "tags" table).</summary>
    public class TagRow
    {
        public int Id { get; set; }
        public string TagName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class CreateTagViewModel
    {
        public string TagName { get; set; } = "";
    }
}
