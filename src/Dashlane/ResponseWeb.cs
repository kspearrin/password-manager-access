// Copyright (C) Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using Newtonsoft.Json;

namespace PasswordManagerAccess.Dashlane.ResponseWeb
{
    internal class Envelope<T>
    {
        [JsonProperty("requestId", Required = Required.Always)]
        public readonly string RequestId;

        [JsonProperty("data", Required = Required.Always)]
        public readonly T Data;
    }

    internal readonly struct VerificationMethods
    {
        [JsonProperty("verification", Required = Required.Always)]
        public readonly VerificationMethod[] Methods;
    }

    internal readonly struct VerificationMethod
    {
        [JsonProperty("type", Required = Required.Always)]
        public readonly string Name;
    }

    internal readonly struct AuthTicket
    {
        [JsonProperty("authTicket", Required = Required.Always)]
        public readonly string Ticket;
    }

    internal readonly struct DeviceInfo
    {
        [JsonProperty("deviceAccessKey", Required = Required.Always)]
        public readonly string AccessKey;

        [JsonProperty("deviceSecretKey", Required = Required.Always)]
        public readonly string SecretKey;

        [JsonProperty("publicUserId", Required = Required.Always)]
        public readonly string UserId;

        // TODO: Make this required
        [JsonProperty("sharingKeys", Required = Required.Default)]
        public readonly SharingKeys SharingKeys;
    }

    internal readonly struct SharingKeys
    {
        // TODO: Make this required
        [JsonProperty("publicKey", Required = Required.Default)]
        public readonly string PublicKey;

        // TODO: Make this required
        [JsonProperty("privateKey", Required = Required.Default)]
        public readonly string PrivateKey;
    }

    internal readonly struct PairingInfo
    {
        [JsonProperty("pairingId", Required = Required.Always)]
        public readonly string PairingId;
    }
}
