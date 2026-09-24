using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Models.Context
{
    public class BudgetAllocation
    {
        public List<KeyMeta> L0Keys = new List<KeyMeta>();
        public List<KeyMeta> L1Keys = new List<KeyMeta>();
        public List<KeyMeta> L2Keys = new List<KeyMeta>();
        public List<KeyMeta> L3Keys = new List<KeyMeta>();
        public List<KeyMeta> L5Keys = new List<KeyMeta>();
        public int MaxHistoryRounds = 6;
        public int MaxRagResults = 3;
        public bool UseFullValue = true;
        public bool UseDiff = false;
    }
}
