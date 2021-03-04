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
        [DisplayName("B2C domain name")]
        public string DomainName { get; set; }

        [Required]
        [DisplayName("Validate only (do not create any objects)")]
        public bool ValidateOnly { get; set; }

        [Required]
        [DisplayName("Create dummy Facebook secret")]
        public bool CreateDummyFacebook { get; set; }
    }
}
