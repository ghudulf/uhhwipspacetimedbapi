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
    public sealed partial class OpenIddictSpacetimeToken
    {
        [DataMember(Name = "Id")]
        public uint Id;
        [DataMember(Name = "OpenIddictTokenId")]
        public string OpenIddictTokenId;
        [DataMember(Name = "AuthorizationId")]
        public uint? AuthorizationId;
        [DataMember(Name = "ApplicationClientId")]
        public string? ApplicationClientId;
        [DataMember(Name = "CreationDate")]
        public ulong? CreationDate;
        [DataMember(Name = "ExpirationDate")]
        public ulong? ExpirationDate;
        [DataMember(Name = "Payload")]
        public string? Payload;
        [DataMember(Name = "Properties")]
        public string? Properties;
        [DataMember(Name = "RedemptionDate")]
        public ulong? RedemptionDate;
        [DataMember(Name = "ReferenceId")]
        public string? ReferenceId;
        [DataMember(Name = "Status")]
        public string? Status;
        [DataMember(Name = "Subject")]
        public string? Subject;
        [DataMember(Name = "Type")]
        public string? Type;

        public OpenIddictSpacetimeToken(
            uint Id,
            string OpenIddictTokenId,
            uint? AuthorizationId,
            string? ApplicationClientId,
            ulong? CreationDate,
            ulong? ExpirationDate,
            string? Payload,
            string? Properties,
            ulong? RedemptionDate,
            string? ReferenceId,
            string? Status,
            string? Subject,
            string? Type
        )
        {
            this.Id = Id;
            this.OpenIddictTokenId = OpenIddictTokenId;
            this.AuthorizationId = AuthorizationId;
            this.ApplicationClientId = ApplicationClientId;
            this.CreationDate = CreationDate;
            this.ExpirationDate = ExpirationDate;
            this.Payload = Payload;
            this.Properties = Properties;
            this.RedemptionDate = RedemptionDate;
            this.ReferenceId = ReferenceId;
            this.Status = Status;
            this.Subject = Subject;
            this.Type = Type;
        }

        public OpenIddictSpacetimeToken()
        {
            this.OpenIddictTokenId = "";
        }
    }
}
