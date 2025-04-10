// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    [SpacetimeDB.Type]
    [DataContract]
    public sealed partial class TotpSecret
    {
        [DataMember(Name = "Id")]
        public uint Id;
        [DataMember(Name = "UserId")]
        public SpacetimeDB.Identity UserId;
        [DataMember(Name = "Secret")]
        public string Secret;
        [DataMember(Name = "CreatedAt")]
        public ulong CreatedAt;
        [DataMember(Name = "IsActive")]
        public bool IsActive;

        public TotpSecret(
            uint Id,
            SpacetimeDB.Identity UserId,
            string Secret,
            ulong CreatedAt,
            bool IsActive
        )
        {
            this.Id = Id;
            this.UserId = UserId;
            this.Secret = Secret;
            this.CreatedAt = CreatedAt;
            this.IsActive = IsActive;
        }

        public TotpSecret()
        {
            this.Secret = "";
        }
    }
}
