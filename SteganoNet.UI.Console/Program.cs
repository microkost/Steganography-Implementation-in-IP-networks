﻿using SteganoNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace SteganoNet.UI.Console
{
    public class Program
    {
        static void Main(string[] args) //static removed
        {
            //method flow:
            //check sw dependencies
            //recognize mode (parametres / wizard), if wizard then:
            //*select role (server / client)
            //*select network interface
            //*choose network parametres
            //*choose steganographic method
            //*add instance of client or server (debug and testing purposes), end if
            //run
            //view immediate info
            //stop
            //analyze results

            bool SimplifyConfigWhenDebug = true; //skips ports and IP address for developing
            bool isHumanDriving = true; //skip asking when is runned from parametres

            System.Console.WriteLine("Welcome in Steganography for IP networks tool.\n");
            if (SteganoNet.Lib.SystemCheck.AreSystemPrerequisitiesDone() == false) //can run?
            {
                System.Console.WriteLine("Nessesary library WinPcap is not installed or PcapDotNet is not present. Check it please and restart.");
                System.Console.WriteLine("Press any key to exit... ");
                System.Console.ReadKey();
                return;
            }

            //config global (pre-initialization)
            string role = "s"; //server or client
            string messageReadable = "VSB - Technical University of Ostrava has long tradition in high quality engineering."; //only for user layer! Never pass to client...
            string messageEncrypted = DataOperationsCrypto.DoCrypto(messageReadable); //send this
            List<int> stegoMethods = new List<int>();
            System.Diagnostics.Process secondWindow = null; //testing solution on same computer
            string runSame = "y"; //user answer for same device

            //network values (pre-initialization)
            string ipSource = "0.0.0.0";
            ushort portSource = NetStandard.GetAvailablePort(11000);
            string ipRemote = "0.0.0.0";
            ushort portRemote = NetStandard.GetAvailablePort(11011);

            if (args.Length == 0 || args == null) //no user parametrized input = configuration WIZARD
            {
                isHumanDriving = true;
                System.Console.WriteLine("Do you want to run configuration wizard? (y/n) y"); //ha-ha             
                System.Console.WriteLine("\tUse IPv4 or IPv6? (4/6) 4"); //ha-ha

                System.Console.Write("\tIs this device (s)erver-receiver or (c)lient-sender? (s/c) ");
                role = System.Console.ReadLine().ToLower();
                System.Console.WriteLine("");

                if (!role.StartsWith("c") && !role.StartsWith("s"))
                {
                    System.Console.Write("\t\tWrong selection, correct are 'c'lient or 's'erver\n\t\tPress Any Key to Exit...");
                    System.Console.ReadKey();
                    return;
                }

                //local IP
                ipSource = ConsoleTools.SelectInterface(); //interactive
                System.Console.WriteLine("");

                if (!SimplifyConfigWhenDebug)
                {
                    //local port                
                    System.Console.Write(String.Format("\tEnter source port: should it be {0}? (y or enter / number) ", portSource));
                    string portSourceNotParsed = System.Console.ReadLine();
                    if (portSourceNotParsed.StartsWith("y") || String.IsNullOrWhiteSpace(portSourceNotParsed)) //not default answer
                    {
                        System.Console.WriteLine(String.Format("\t\tUsed port is: {0}", portSource)); //without change
                    }
                    else
                    {
                        if (!ushort.TryParse(portSourceNotParsed.ToString(), out ushort parsed))
                        {
                            portSource = NetStandard.GetAvailablePort(48000);
                        }
                        else
                        {
                            portSource = parsed;
                        }
                        System.Console.WriteLine(String.Format("\t\tUsed port is: {0}", portSource));
                    }

                    if (String.Equals("c", role))
                    {
                        //remote IP address
                        String[] ipBytes = ipSource.Split('.'); uint byte1 = Convert.ToUInt32(ipBytes[0]); uint byte2 = Convert.ToUInt32(ipBytes[1]); uint byte3 = Convert.ToUInt32(ipBytes[2]);
                        uint byte4 = Convert.ToUInt32(ipBytes[3]) + 0; //MAGIC NUMBER for IP higher 1 than local
                        ipRemote = String.Format("{0}.{1}.{2}.{3}", byte1, byte2, byte3, byte4);
                        System.Console.Write(String.Format("\tEnter remote host IP address: should it be {0}?  (y or enter / ip address) ", ipRemote));
                        string ipremoteNotParsed = System.Console.ReadLine();
                        if (ipremoteNotParsed.StartsWith("y") || String.IsNullOrWhiteSpace(ipremoteNotParsed)) //not default answer
                        {
                            System.Console.WriteLine(String.Format("\t\tUsed remote ip is: {0}", ipRemote)); //without change
                        }
                        else
                        {
                            if (System.Net.IPAddress.TryParse(ipremoteNotParsed.ToString(), out System.Net.IPAddress parsed))
                            {
                                ipRemote = parsed.ToString();
                            }
                            System.Console.WriteLine(String.Format("\t\tUsed remote ip is: {0}", ipRemote));
                        }

                        //remote port
                        System.Console.Write(String.Format("\tEnter remote port: should it be {0}? (y or enter / number) ", portRemote));
                        string portRemoteNotParsed = System.Console.ReadLine();
                        if (portRemoteNotParsed.StartsWith("y") || String.IsNullOrWhiteSpace(portRemoteNotParsed)) //not default answer
                        {
                            System.Console.WriteLine(String.Format("\t\tUsed port is: {0}", portRemote)); //without change
                        }
                        else
                        {
                            if (!ushort.TryParse(portRemoteNotParsed.ToString(), out ushort parsed))
                            {
                                portRemote = NetStandard.GetAvailablePort(48000);
                            }
                            else
                            {
                                portRemote = parsed;
                            }
                            System.Console.WriteLine(String.Format("\t\tUsed port is: {0}", portRemote));
                        }
                    }
                    System.Console.WriteLine("");
                }
                else
                {
                    ipRemote = ipSource;
                    System.Console.WriteLine("\tSkipped detailed configuration info.\n");
                }

                stegoMethods = ConsoleTools.SelectStegoMethods(); //which methods are used (interactive)                                
            }
            else //skip the wizard, source from parametres
            {
                isHumanDriving = false;
                System.Console.WriteLine("Do you want to run configuration wizard? (y/n) n\n\nUsing following parametres as settings: ");

                /*
                 * VALID PARAMS: (separator is space)
                 * -role c
                 * -ip 192.168.1.216 
                 * -port 11011
                 * -ipremote 192.168.1.216
                 * -portremote 11001
                 * -methods: 301,302
                 * -runsame: n
                 * -message: ""
                 */

                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "-role":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                role = args[i].Substring(0, 1).ToLower(); //first char only
                                break;
                            }

                        case "-ip":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                ipSource = args[i];
                                break;
                            }
                        case "-port":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                ushort.TryParse(args[i].ToString(), out ushort parsed); //parsing
                                portSource = parsed;
                                break;
                            }
                        case "-ipremote":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                ipRemote = args[i];
                                break;
                            }
                        case "-portremote":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                ushort.TryParse(args[i].ToString(), out ushort parsed); //parsing
                                portRemote = parsed;
                                break;
                            }
                        case "-methods":
                            {
                                i++;
                                int[] nums = Array.ConvertAll(args[i].Split(','), int.Parse);
                                stegoMethods = nums.ToList();
                                break;
                            }
                        case "-runsame":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                runSame = args[i].Substring(0, 1).ToLower(); //first char only
                                break;
                            }
                        case "-message":
                            {
                                i++;
                                if (args.Length <= i) throw new ArgumentException(args[i]);
                                messageReadable = args[i];
                                messageEncrypted = DataOperationsCrypto.DoCrypto(messageReadable);
                                break;
                            }

                            //TODO parsing of -h HELP parameter which shows details...
                    }
                    System.Console.WriteLine(String.Format("\t{0} value: {1}", args[i - 1], args[i])); //show what arrived, not what is in variables
                }
            }

            //offers running another window with client or server
            if (!runSame.StartsWith("n")) //runSame is difeerent than n (asking for first time)
            {
                System.Console.Write("\nDo you want to run opposite node on same device for testing? (y/n) ");
                runSame = System.Console.ReadLine();
                if (runSame.StartsWith("y") || runSame.StartsWith("Y") || String.IsNullOrWhiteSpace(runSame))
                {
                    string roleToRun = (role.StartsWith("c")) ? "s" : "c";
                    string arguments = String.Format("-role {0} -ip {1} -port {2} -ipremote {3} -portremote {4} -methods {5} -runsame {6} -message \"{7}\"", roleToRun, ipRemote, portRemote, ipSource, portSource, string.Join(",", stegoMethods.Select(n => n.ToString()).ToArray()), "n", messageReadable); //inverted settings
                    secondWindow = System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, arguments);
                }
            }

            if (String.Equals("s", role)) //its server
            {
                //prepare server
                NetReceiverServer rs = new NetReceiverServer(ipSource, portSource, ipRemote, portRemote); //old way
                //NetReceiverServer rs = new NetReceiverServer(ipSource, portSource);
                rs.StegoUsedMethodIds = stegoMethods;

                //prepare thread for server
                ThreadStart threadDelegate = new ThreadStart(rs.Listening);
                Thread receiverServerThread = new Thread(threadDelegate);
                //Thread receiverServerThread = new Thread(rs.Listening);
                receiverServerThread.Name = "ListeningThread";
                receiverServerThread.IsBackground = true;
                receiverServerThread.Start();

                //server activity output
                System.Console.WriteLine("\nShowing server running information. Press ESC to stop when message is received.");
                do
                {
                    while (!System.Console.KeyAvailable)
                    {
                        ConsoleTools.WriteInfoConsole(rs);
                    }
                } while (System.Console.ReadKey(true).Key != ConsoleKey.Escape);

                rs.Terminate = true;
                receiverServerThread.Abort(); //stop server thread
                receiverServerThread.Join(); //needed?

                messageEncrypted = rs.GetSecretMessage();
                messageReadable = DataOperationsCrypto.ReadCrypto(messageEncrypted); //mock

                System.Console.WriteLine("");
                System.Console.WriteLine(String.Format("Received secret message are: (per line)\n{0}", messageReadable));
            }
            else if (String.Equals("c", role)) //its client
            {
                System.Console.WriteLine(String.Format("\n\tActual message: \n\t\t{0}", messageReadable));

                if (isHumanDriving) //skip interactive question when is from parametres
                {
                    System.Console.Write("\tDo you want to change message? (y/n) ");
                    runSame = System.Console.ReadLine();
                    if (runSame.StartsWith("y") || runSame.StartsWith("Y") || String.IsNullOrWhiteSpace(runSame))
                    {
                        System.Console.Write("\tEnter secret message: ");
                        messageReadable = System.Console.ReadLine();
                        messageEncrypted = DataOperationsCrypto.DoCrypto(messageReadable);
                    }
                }

                //prepare client
                NetSenderClient sc = new NetSenderClient(ipSource, portSource, ipRemote, portRemote);
                sc.SecretMessage = messageEncrypted; //never pass in messageReadable! It works, but from principe...
                sc.StegoUsedMethodIds = stegoMethods;

                //prepare thread for client
                ThreadStart threadDelegate = new ThreadStart(sc.Speaking);
                Thread senderClientThread = new Thread(threadDelegate);
                senderClientThread.Name = "SpeakingThread";
                senderClientThread.IsBackground = true;
                senderClientThread.Start();

                //client activity output
                isHumanDriving = true; //TODO debug only
                if (isHumanDriving)
                {
                    System.Console.WriteLine(String.Format("\nSending should take around {0} s", ConsoleTools.HowLongIsTransferInMs(messageEncrypted, stegoMethods) / 1000));
                    System.Console.WriteLine("\nShowing client running information. Press ESC to stop when message is received.");
                    do
                    {
                        while (!System.Console.KeyAvailable)
                        {
                            ConsoleTools.WriteInfoConsole(sc);
                        }

                    } while (System.Console.ReadKey(true).Key != ConsoleKey.Escape); //show when is human is watching
                }
                else
                {
                    //do not show output but leave some time for run of sending thread
                    int sleepTime = ConsoleTools.HowLongIsTransferInMs(messageEncrypted, stegoMethods);
                    System.Console.WriteLine(String.Format("\nOutput suppressed, waiting {0} s ({1} min) for end...", sleepTime / 1000, sleepTime / 1000 / 60));
                    Thread.Sleep(sleepTime);
                }

                sc.Terminate = true;
                senderClientThread.Abort(); //stop client thread
                senderClientThread.Join(); //needed?
            }
            else //catch
            {
                System.Console.WriteLine("\nSorry, I didnt understand your commands. Start again...");
            }

            try //handling opened console windows
            {
                if (secondWindow != null)
                {
                    System.Console.WriteLine("\nWaiting for end of second window. Please close it manually.");
                }
                secondWindow.WaitForExit(); //correct ending of opened window
            }
            catch (NullReferenceException)
            {
                //System.Console.WriteLine("No another window opened...");
            }

            //making executable command (WIN+R) and copy-paste
            System.Console.Write(String.Format("\nRun same scenario again with command: \n{0} ", Assembly.GetExecutingAssembly().CodeBase/*, Path.GetFileName(Assembly.GetExecutingAssembly().CodeBase)*/));
            System.Console.WriteLine(String.Format("-role {0} -ip {1} -port {2} -ipremote {3} -portremote {4} -methods {5} -runsame {6} -message \"{7}\"", role, ipSource, portSource, ipRemote, portRemote, string.Join(",", stegoMethods.Select(n => n.ToString()).ToArray()), "n", "sample"));

            if (isHumanDriving) //dont ask when script
            {
                System.Console.WriteLine("\nThat's all! Thank you for using Steganography for IP networks tool. Press any key to exit...");
                System.Console.ReadKey();
            }


            //TODO if server then return result for pipelining
            return;
        }
    }

}
