 using System.Text;
using SpacetimeDB;

public static partial class Module
{ 
    
    [SpacetimeDB.Table(Public = true)]
    public partial class Discounts
    {
        [PrimaryKey]
        public uint DiscountId;
        public string DiscountType;
        public double DiscountPercentage;
        public ulong StartDate;
        public ulong EndDate;
        public bool IsActive;
        public string? Description;
        public string? RequiredDocuments;
        public string? CreatedBy;
        public ulong CreatedAt;
        public string? UpdatedBy;
        public ulong? UpdatedAt;
    }

    
}