// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using PasswordManagerAccess.Dashlane;
using PasswordManagerAccess.Example.Common;

namespace Example
{
    class TextUi: Ui
    {
        public override Passcode ProvideGoogleAuthPasscode(int attempt)
        {
            if (attempt > 0)
                Bad("Google Authenticator code is invalid, try again");

            return GetPasscode($"Please enter Google Authenticator code {ToCancel}");
        }

        //
        // Private
        //

        private static Passcode GetPasscode(string prompt)
        {
            var passcode = GetAnswer(prompt);
            return passcode == "" ? Passcode.Cancel : new Passcode(passcode, GetRememberMe());
        }

        private static string GetAnswer(string prompt)
        {
            Console.WriteLine(prompt);
            Console.Write("> ");
            var input = Console.ReadLine();

            return input == null ? "" : input.Trim();
        }

        private static bool GetRememberMe()
        {
            var remember = GetAnswer("Remember this device?").ToLower();
            return remember == "y" || remember == "yes";
        }

        private static void Bad(string text)
        {
            WriteLine(ConsoleColor.Red, text);
        }

        private static void WriteLine(ConsoleColor color, string text)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        private const string ToCancel = "or just press ENTER to cancel";
    }

    static class Program
    {
        static void Main(string[] args)
        {
            var config = Util.ReadConfig();

            var username = config["username"];
            var password = config["password"];

            // The UKI is optional.
            var uki = config.ContainsKey("uki") ? config["uki"] : "";

            // It seems we don't have an UKI. We need one to authenticate with the server.
            // An UKI is a device id that is registered with the Dashlane server. There are
            // two ways to obtain one.

            // 1. On a machine that has a Dashlane client installed we could rummage through
            // the settings database and find the UKI that is used by the client. This way
            // we can pretend to be that client and silently authenticate with the server.
            if (uki == "")
            {
                try
                {
                    Console.WriteLine("No UKI is specified. Looking for the local Dashlane");
                    Console.WriteLine("settings database (profile name: {0})", username);

                    uki = Import.ImportUki(username, password);

                    Console.WriteLine("Found an UKI in the local database: {0}", uki);
                }
                catch (ImportException e)
                {
                    Console.WriteLine("Could not import the UKI from the local Dashlane setting)");
                    Console.WriteLine("Error: {0} ({1})", e.Message, e.Reason);
                }
            }

            // 2. Alternatively we could try to generate a new UKI and register it with the
            // server. The process is interactive and is made up of two steps. Step one
            // initiates the process and triggers an email to be sent to the user and the
            // registered email address with a security token.
            // In step 2 the token along with the machine name and the new UKI is registered
            // with the server. After these two steps the new UKI could be used to authenticate
            // with the Dashlane server and fetch the vault.
            if (uki == "")
            {
                try
                {
                    // Request a security token to be sent to the user's email address.
                    Console.WriteLine("Initiating a new UKI registration. Requesting a security token.");
                    Vault.RegisterUkiStep1(username);

                    // Ask the user to enter the token.
                    Console.Write("Enter the token sent by email: ");
                    Console.Out.Flush();
                    var token = Console.ReadLine().Trim();

                    // Generate a new UKI.
                    var newUki = Uki.Generate();
                    Console.WriteLine("Generated a new UKI: {0}", newUki);

                    // Register all that with the server.
                    Console.WriteLine("Registering the new UKI under the name of 'dashlane-sharp'");
                    Vault.RegisterUkiStep2(username, "dashlane-sharp", newUki, token);

                    // Great success!
                    uki = newUki;
                    Console.WriteLine("Registration successful");
                    Console.WriteLine("Save this UKI for later access: {0}", uki);
                }
                catch (RegisterException e)
                {
                    Console.WriteLine("Could not register the new UKI with the server.");
                    Console.WriteLine("Error: {0} ({1})", e.Message, e.Reason);
                }
            }

            // We still don't have a valid UKI. Cannot proceed any further.
            if (uki == "")
            {
                Console.WriteLine("It's impossible to continue with out a valid UKI. Exiting.");
                return;
            }

            // Now, when we have a registered UKI we can try to open the vault.
            try
            {
                // Fetch and parse first.
                Console.WriteLine("Fetching and parsing the remote vault");
                var vault = Vault.Open(username, password, uki, new TextUi());

                // And then dump the accounts.
                Console.WriteLine("The vault has {0} account(s) in it:", vault.Accounts.Length);
                for (var i = 0; i < vault.Accounts.Length; i++)
                {
                    var account = vault.Accounts[i];
                    Console.WriteLine(
                        "{0}: {1} {2} {3} {4} {5}",
                        i + 1,
                        account.Name,
                        account.Username,
                        account.Password,
                        account.Url,
                        account.Note);
                }
            }
            catch (PasswordManagerAccess.Common.BaseException e)
            {
                Util.PrintException(e);
            }
            catch (ParseException e)
            {
                Console.WriteLine("Could not parse the vault");
                Console.WriteLine("Error: {0} ({1})", e.Message, e.Reason);
            }
        }
    }
}
