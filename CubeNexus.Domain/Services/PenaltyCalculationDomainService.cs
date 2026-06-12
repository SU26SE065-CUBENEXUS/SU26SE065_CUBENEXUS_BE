using System;
using System.Collections.Generic;
using System.Linq;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Domain.Services;

public class PenaltyCalculationDomainService
{
    public void CalculateTraditionalResult(Result result, PenaltyType? penaltyType)
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
    }

    public void CalculateMedleyResult(Result parentResult, List<(MedleyResultDetail detail, PenaltyType? penalty)> details)
    {
        bool hasDnf = false;
        int totalFinalTimeMs = 0;

        foreach (var item in details)
        {
            var detail = item.detail;
            var penalty = item.penalty;

            if (penalty != null)
            {
                if (penalty.IsDisqualified || penalty.Code == "DNF")
                {
                    detail.IsDnf = true;
                    detail.FinalTimeMs = null;
                    hasDnf = true;
                }
                else
                {
                    detail.IsDnf = false;
                    detail.FinalTimeMs = detail.RawTimeMs + penalty.TimeAdditionMs;
                    if (detail.FinalTimeMs.HasValue)
                    {
                        totalFinalTimeMs += detail.FinalTimeMs.Value;
                    }
                    else
                    {
                        hasDnf = true;
                    }
                }
            }
            else
            {
                detail.IsDnf = false;
                detail.FinalTimeMs = detail.RawTimeMs;
                if (detail.FinalTimeMs.HasValue)
                {
                    totalFinalTimeMs += detail.FinalTimeMs.Value;
                }
                else
                {
                    hasDnf = true;
                }
            }
        }

        if (hasDnf)
        {
            parentResult.IsDnf = true;
            parentResult.FinalTimeMs = null;
        }
        else
        {
            parentResult.IsDnf = false;
            parentResult.FinalTimeMs = totalFinalTimeMs;
        }
    }
}
