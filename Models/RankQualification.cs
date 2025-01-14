﻿namespace BeatLeader_Server.Models
{
    public class RankQualification
    {
        public int Id { get; set; }
        public int Timeset { get; set; }
        public string RTMember { get; set; }

        public int CriteriaTimeset { get; set; }
        public int CriteriaMet { get; set; }
        public string? CriteriaChecker { get; set; }
        public string? CriteriaCommentary { get; set; }

        public bool MapperAllowed { get; set; }
        public string? MapperId { get; set; }

        public bool MapperQualification { get; set; }

        public int ApprovalTimeset { get; set; }
        public bool Approved { get; set; }
        public string? Approvers { get; set; }

        public ModifiersMap? Modifiers { get; set; }

        public ICollection<QualificationChange>? Changes { get; set; }
    }
}
