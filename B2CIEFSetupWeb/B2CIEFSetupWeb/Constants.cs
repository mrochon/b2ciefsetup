using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace B2CIEFSetupWeb
{
    public static class Constants
    {
        public static readonly string[] Scopes =
        {
            "Application.ReadWrite.All",
            "TrustFrameworkKeySet.ReadWrite.All",
            "Directory.AccessAsUser.All",
        };
    }
}
