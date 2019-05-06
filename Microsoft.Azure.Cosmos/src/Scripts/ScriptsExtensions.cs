﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Extensions to interact with Scripts.
    /// </summary>
    /// <seealso cref="CosmosStoredProcedure"/>
    /// <seealso cref="CosmosTrigger"/>
    /// <seealso cref="CosmosUserDefinedFunction"/>
    public static class ScriptsExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="cosmosContainer"></param>
        /// <returns></returns>
        public static CosmosScripts GetScripts(this CosmosContainer cosmosContainer)
        {
            return new CosmosScriptsCore((CosmosContainerCore) cosmosContainer);
        }
    }
}
