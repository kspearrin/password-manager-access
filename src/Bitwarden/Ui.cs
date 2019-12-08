// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

namespace PasswordManagerAccess.Bitwarden
{
    public abstract class Ui
    {
        // The UI will no longer be used and could be closed
        public abstract void Close();

        public enum MfaMethod
        {
            // Always available
            Cancel,

            GoogleAuth,
            Email,
            Duo,
            YubiKey,
            U2f,
        }

        // To cancel return Method.Cancel (always available)
        public abstract MfaMethod ChooseMfaMethod(MfaMethod[] availableMethods);

        public class Passcode
        {
            public readonly string Code;
            public readonly bool RememberMe;

            public Passcode(string code, bool rememberMe)
            {
                Code = code;
                RememberMe = rememberMe;
            }
        }

        // To cancel any of these return null
        public abstract Passcode ProvideGoogleAuthPasscode();
        public abstract Passcode ProvideEmailPasscode(string emailHint);
        public abstract Passcode ProvideYubiKeyPasscode();

        //
        // Duo
        //

        public enum DuoFactor
        {
            Push,
            Call,
            Passcode,
            SendPasscodesBySms,
        }

        public class DuoDevice
        {
            public readonly string Id;
            public readonly string Name;
            public readonly DuoFactor[] Factors;

            public DuoDevice(string id, string name, DuoFactor[] factors)
            {
                Id = id;
                Name = name;
                Factors = factors;
            }
        }

        public class DuoChoice
        {
            public readonly DuoDevice Device;
            public readonly DuoFactor Factor;
            public readonly bool RememberMe;

            public DuoChoice(DuoDevice device, DuoFactor factor, bool rememberMe)
            {
                Device = device;
                Factor = factor;
                RememberMe = rememberMe;
            }
        }

        public enum DuoStatus
        {
            Success,
            Error,
            Info,
        }

        // To cancel return null
        public abstract DuoChoice ChooseDuoFactor(DuoDevice[] devices);

        // To cancel return null or blank
        public abstract string ProvideDuoPasscode(DuoDevice device);

        // This updates the UI with the messages from the server.
        public abstract void UpdateDuoStatus(DuoStatus status, string text);
    }
}
