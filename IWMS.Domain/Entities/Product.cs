using IWMS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities
{
    public class Product:BaseEntity
    {
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? BarCode { get; set; }
        public UnitOfMeasure BaseUnit { get; set; } = UnitOfMeasure.Each;
        public decimal? CostPrice { get; set; }
        public decimal? ReorderLevel { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid CategoryId { get; set; }
        public Category Category { get; set; } = null!;
        public List<InventoryBalance> Balances { get; set; } = new();
    }
}
