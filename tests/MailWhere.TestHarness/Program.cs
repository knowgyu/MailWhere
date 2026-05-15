using MailWhere.Core.Analysis;
using MailWhere.Core.Domain;

var analyzer = new RuleBasedFollowUpAnalyzer();
var samples = new[]
{
    new EmailSnapshot("sample-1", DateTimeOffset.Now, "김OO", "자료 요청", "내일까지 자료 검토 후 회신 부탁드립니다."),
    new EmailSnapshot("sample-2", DateTimeOffset.Now, "newsletter", "공지", "FYI 참고용 뉴스레터입니다."),
    new EmailSnapshot("sample-3", DateTimeOffset.Now, "manager", "Action required", "Please review and send the update by tomorrow.")
};

foreach (var sample in samples)
{
    var result = await analyzer.AnalyzeAsync(sample);
    Console.WriteLine($"{sample.Subject}: {result.Disposition} {result.Confidence:P0} {result.Reason}");
}
