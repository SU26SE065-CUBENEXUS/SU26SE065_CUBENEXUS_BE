using System;
using System.Collections.Generic;
using System.Linq;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Domain.Services;

public class PenaltyCalculationDomainService
{
    public void CalculateTraditionalResult(Result result, PenaltyType? penaltyType, int? timeLimitMs = null)
    {
        if (penaltyType != null)
        {
            if (penaltyType.IsDisqualified || penaltyType.Code == "DNF")
            {
                result.IsDnf = true;
                result.FinalTimeMs = null;
            }
            else
            {
                result.IsDnf = false;
                result.FinalTimeMs = result.RawTimeMs + penaltyType.TimeAdditionMs;
            }
        }
        else
        {
            result.IsDnf = false;
            result.FinalTimeMs = result.RawTimeMs;
        }

        // Auto DNF if RawTime or FinalTime meets or exceeds TimeLimit
        if (!result.IsDnf && timeLimitMs.HasValue && timeLimitMs.Value > 0)
        {
            var effectiveTime = result.FinalTimeMs ?? result.RawTimeMs;
            if (effectiveTime.HasValue && effectiveTime.Value >= timeLimitMs.Value)
            {
                result.IsDnf = true;
                result.FinalTimeMs = null;
            }
        }
    }

    public void CalculateMedleyResult(Result parentResult, List<(MedleyResultDetail detail, PenaltyType? penalty)> details, int? timeLimitMs = null)
    {
        // Medley Relay: tổng thời gian nằm trong detail đầu tiên (idx=0).
        // Các detail còn lại chỉ ghi nhận scramble dùng cho từng khối, RawTimeMs = null.
        var firstItem = details.FirstOrDefault();
        if (firstItem == default)
        {
            parentResult.IsDnf = false;
            parentResult.RawTimeMs = 0;
            parentResult.FinalTimeMs = 0;
            return;
        }

        var firstDetail = firstItem.detail;
        var firstPenalty = firstItem.penalty;

        // Set sub-puzzle details (idx > 0) as IsDnf=false, FinalTimeMs=null (không theo dõi riêng)
        foreach (var (detail, _) in details.Skip(1))
        {
            detail.IsDnf = false;
            detail.FinalTimeMs = null;
            detail.RawTimeMs = null;
        }

        // Tính tổng dựa vào detail đầu tiên
        int rawTotal = firstDetail.RawTimeMs ?? 0;
        parentResult.RawTimeMs = rawTotal;

        if (firstPenalty != null && (firstPenalty.IsDisqualified || firstPenalty.Code == "DNF"))
        {
            firstDetail.IsDnf = true;
            firstDetail.FinalTimeMs = null;
            parentResult.IsDnf = true;
            parentResult.FinalTimeMs = null;
        }
        else if (firstPenalty != null)
        {
            firstDetail.IsDnf = false;
            firstDetail.FinalTimeMs = rawTotal + firstPenalty.TimeAdditionMs;
            parentResult.IsDnf = false;
            parentResult.FinalTimeMs = firstDetail.FinalTimeMs ?? rawTotal;
        }
        else
        {
            firstDetail.IsDnf = false;
            firstDetail.FinalTimeMs = rawTotal;
            parentResult.IsDnf = false;
            parentResult.FinalTimeMs = rawTotal;
        }

        // Auto DNF if RawTime or FinalTime meets or exceeds TimeLimit
        if (!parentResult.IsDnf && timeLimitMs.HasValue && timeLimitMs.Value > 0)
        {
            var effectiveTime = parentResult.FinalTimeMs ?? parentResult.RawTimeMs;
            if (effectiveTime.HasValue && effectiveTime.Value >= timeLimitMs.Value)
            {
                parentResult.IsDnf = true;
                parentResult.FinalTimeMs = null;
            }
        }
    }
}
