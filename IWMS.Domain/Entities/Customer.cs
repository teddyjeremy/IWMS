using IWMS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace IWMS.Domain.Entities
{
    public class Customer:BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public List<SalesOrder> SalesOrders { get; set; } = new();
    }
}
