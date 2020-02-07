using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace B2CIEFSetupWeb.Models
{
    public class SetupRequest
    {
        [Required]
        [MaxLength(256), MinLength(4)]
        [RegularExpression("^([a-zA-Z0-9]+)$", ErrorMessage = "Invalid tenant name")]
        [DisplayName("Domain name")]
        public string DomainName { get; set; }
    }
}
