using System.Security.Cryptography;
using System.Text;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class StateLicensingEntrySeed
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        var existing = (await db.StateLicensingEntries.Where(entry => entry.SeedKey != null)
            .Select(entry => entry.SeedKey!).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        foreach (var state in StateLicensingReferenceService.GetStates())
        {
            foreach (var question in state.Questions)
            {
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{state.Name}\n{question.Question}")));
                if (!existing.Add(key))
                {
                    continue;
                }

                var notes = string.Join("\n\n", (question.Citations ?? []).Select(citation =>
                    $"{citation.Title}\n{citation.Url}\n{citation.RepositoryPath}\nReviewed: {citation.ReviewedOn:yyyy-MM-dd}\n{citation.CurrencyNote}"));
                db.StateLicensingEntries.Add(new StateLicensingEntry
                {
                    State = state.Name, SeedKey = key, Question = question.Question, Answer = question.Answer,
                    SourceNotes = notes, PublishedQuestion = question.Question, PublishedAnswer = question.Answer,
                    PublishedAtUtc = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
