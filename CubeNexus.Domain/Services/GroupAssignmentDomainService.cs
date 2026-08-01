using System;
using System.Collections.Generic;
using System.Linq;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Domain.Services;

public class GroupAssignmentDomainService
{
    public List<GroupCompetitorAssignment> AssignGroups(
        Guid eventId,
        int roundNumber,
        List<OfflineRegistrationEvent> registeredEvents,
        int competitorsPerGroup,
        int stationCount)
    {
        if (competitorsPerGroup <= 0)
            throw new ArgumentException("Competitors per group must be greater than zero.", nameof(competitorsPerGroup));
        if (stationCount <= 0)
            throw new ArgumentException("Station count must be greater than zero.", nameof(stationCount));

        // For Round 1: Sort by SeedTimeMs ASC (NULLS LAST)
        // For Round > 1: Preserve incoming rank order (do NOT re-sort by original pre-tournament SeedTimeMs!)
        List<OfflineRegistrationEvent> sorted;
        if (roundNumber == 1)
        {
            sorted = registeredEvents
                .OrderBy(e => e.SeedTimeMs.HasValue ? 0 : 1)
                .ThenBy(e => e.SeedTimeMs)
                .ToList();
        }
        else
        {
            sorted = registeredEvents.ToList();
        }

        var assignments = new List<GroupCompetitorAssignment>();
        int competitorIndex = 0;
        int groupNumber = 1;

        while (competitorIndex < sorted.Count)
        {
            var groupCompetitors = sorted.Skip(competitorIndex).Take(competitorsPerGroup).ToList();
            string groupName = $"Group {groupNumber}";

            // Shuffle available station numbers (1..stationCount) randomly for fair and random station allocation
            var randomStations = Enumerable.Range(1, stationCount)
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            for (int i = 0; i < groupCompetitors.Count; i++)
            {
                var regEvent = groupCompetitors[i];
                // Station number is assigned randomly from shuffled stations
                int stationNumber = randomStations[i % randomStations.Count];

                assignments.Add(new GroupCompetitorAssignment
                {
                    RegistrationEvent = regEvent,
                    GroupName = groupName,
                    StationNumber = stationNumber,
                    GroupNumber = groupNumber
                });
            }

            competitorIndex += competitorsPerGroup;
            groupNumber++;
        }

        return assignments;
    }
}

public class GroupCompetitorAssignment
{
    public OfflineRegistrationEvent RegistrationEvent { get; set; } = null!;
    public string GroupName { get; set; } = string.Empty;
    public int StationNumber { get; set; }
    public int GroupNumber { get; set; }
}
