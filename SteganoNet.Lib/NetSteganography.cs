﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PcapDotNet.Packets.Dns;
using PcapDotNet.Packets.Http;
using PcapDotNet.Packets.Icmp;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

namespace SteganoNetLib
{
    public static class NetSteganography //not static
    {
        //magic numbers dialer (never use numbers directly outside this class, if needed use like "IcmpGenericPing" value)
        public const int IpRangeStart = 300;
        public const int IpRangeEnd = 329;
        public const int IpIdentificationMethod = 303;
        public const int IcmpRangeStart = 330;
        public const int IcmpRangeEnd = 359;
        public const int IcmpGenericPing = 331;
        public const int NetworkRangeStart = IpRangeStart; //used in test
        public const int NetworkRangeEnd = 399; //used in test
        public const int TcpRangeStart = 450;
        public const int TcpRangeEnd = 499;
        public const int DnsRangeStart = 700;
        public const int DnsRangeEnd = 729;
        public const int HttpRangeStart = 730;
        public const int HttpRangeEnd = 759;

        //TODO missing const values for usedBit size. Need to be checked manually if value fits in writing and readning method.

        private static Random rand = new Random();
        private static ushort SequenceNumber = (ushort)DateTime.Now.Ticks; //for legacy usage        
        private static string Identification = ""; //receiver's holding value place

        public static Dictionary<int, string> GetListStegoMethodsIdAndKey() //service method
        {
            /* 
             * Logic of ID integers: (do not use xx0, keep them like group name)
             * 0xx > debug & developer
             * 1xx > physical layer
             * 2xx > data-link layer
             * 3xx > network layer 
             * 4xx > transport layer 
             * 7xx > session, presentation and application layer
             * 8xx > other methods like time channel
             * 
             * dash '-' is splitter for parsing capacity size of method! always at the end with size and unit ' - 100b' for 100 bits in method
             * inform user about settings in [] brackets like exact delay if possible (constants)
             */

            //for details read file MethodDescription.txt, keep it updated if changing following list!
            Dictionary<int, string> listOfStegoMethods = new Dictionary<int, string>
            {
                { 301, "IP Type of service / DiffServ (agresive) - " + GetMethodCapacity(301) + "b" },
                { 302, "IP Type of service / DiffServ - " + GetMethodCapacity(302) + "b" },
                { 303, String.Format("IP Identification [delay {0} s] - {1}b", (double)NetSenderClient.IpIdentificationChangeSpeedInMs/1000, GetMethodCapacity(303) )}, //adding exact time value to the name
                { 305, "IP flag (MF + offset (when applicable)) - " + GetMethodCapacity(305) + "b" },
                //---
                { 331, String.Format("ICMP ping (standard) [delay {0} s] - 0b", (double)NetSenderClient.delayIcmp/1000) },
                { 333, "ICMP ping (Identifier) - 16b" }, //TODO should be set on start of transaction and not changed in the time
                { 335, "ICMP ping (Sequence number) - 16b" }, //TODO is actually changing all the time, improve
                //---
                //{ 451, "TCP (standard) - 0b" }, //TODO
                    //{ 453, "TCP (ISN) - 32b" }, //TODO
                    //455 TCP Urgent pointer
                    //TCP Options - Timestamp                    
                //---
                { 701, String.Format("DNS request (standard over UDP) [delay {0} s] - 0b", (double)NetSenderClient.delayDns/1000) },
                { 703, "DNS request (transaction id) - 16b" },
                    //707 DNS flags https://tools.ietf.org/html/rfc1035#section-4.1.1, standard 0x00000100
                    //705 query contains answer, probably not valid...
                //---
                { 731, "HTTP GET (over TCP) - 0b" }, //TODO
                { 733, "HTTP GET facebook picture - 64b" }, //TODO
                //HTTP Entity tag headers 
                        
                //TODO time channel! (ttl methods, resting value is magic value, round trip timer) (ping delay or TCP delay)
                //https://github.com/PcapDotNet/Pcap.Net/wiki/Pcap.Net-Tutorial-Gathering-Statistics-on-the-network-traffic
                //TODO TTL usage or similar (count TTL and use some value under as rest...)
            };

            return listOfStegoMethods; //DO NOT DYNAMICAL MODIFY THAT LIST DURING RUNNING
        }

        //service method
        public static int GetMethodCapacity(int id) //manually edited capacity of each method
        {
            Dictionary<int, int> capacity = new Dictionary<int, int>()
            {
                { 301, 8 },
                { 302, 2 },
                { 303, 16 },
                { 305, 13 },
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
                //{ ,},
            };

            if (capacity.ContainsKey(id))
            {
                return capacity[id];
            }
            else
            {
                return 0;
            }
        }

        //service method
        public static List<int> GetListMethodsId(int startValue, int endValue, Dictionary<int, string> stegoMethodsIdAndKey) //returns ids of methods from certain range when source specified
        {
            List<int> source = stegoMethodsIdAndKey.Keys.ToList(); //separate ids from dictionary

            if (source == null)
            {
                source = GetListStegoMethodsIdAndKey().Keys.ToList(); //TODO test, is dangerous when no list in GetListStegoMethodsIdAndKey
            }

            IEnumerable<int> listOfIpMethods = from num in source where num >= startValue && num <= endValue select num;
            return listOfIpMethods.ToList();
        }

        //---------------------------------------------------------------------------------------------------------------------

        //ip layer methods               
        public static Tuple<IpV4Layer, string> SetContent3Network(IpV4Layer ip, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc = null, bool firstAndResetRun = false) //SENDER
        {
            if (ip == null) { return null; } //extra protection
            List<string> BlocksOfSecretSent = new List<string>(); //SHOULD BE REMOVED

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case 301: //IP (Type of service / DiffServ) agresive //SENDER
                        {
                            sc.AddInfoMessage("3IP: method " + methodId);
                            int usedbits = GetMethodCapacity(methodId);
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                BlocksOfSecretSent.Add(partOfSecret);
                                //add check for 0
                                ip.TypeOfService = Convert.ToByte(partOfSecret, 2);
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    string transfer = (secret[0].Equals("0")) ? "0" : secret; //using rest, but it cant start with zero
                                    BlocksOfSecretSent.Add(transfer);
                                    ip.TypeOfService = Convert.ToByte(transfer, 2);
                                    secret = secret.Remove(0, transfer.Length);

                                }
                                return new Tuple<IpV4Layer, string>(ip, secret); //nothing more                               
                            }
                            break;
                        }
                    case 302: //IP (Type of service / DiffServ) //SENDER
                        {
                            sc.AddInfoMessage("3IP: method " + methodId);
                            int usedbits = GetMethodCapacity(methodId);
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                BlocksOfSecretSent.Add(partOfSecret);
                                //add check for 0
                                ip.TypeOfService = Convert.ToByte(partOfSecret, 2);
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    string transfer = (secret[0].Equals("0")) ? "0" : secret; //using rest, but it cant start with zero
                                    BlocksOfSecretSent.Add(transfer);
                                    ip.TypeOfService = Convert.ToByte(transfer, 2); //using rest + padding removed .PadLeft(usedbits, '0')
                                    secret = secret.Remove(0, transfer.Length);
                                }
                                return new Tuple<IpV4Layer, string>(ip, secret); //nothing more          
                            }
                            break;
                        }
                    case 303: //IP (Identification) //SENDER
                        {
                            int usedbits = GetMethodCapacity(methodId);
                            if (firstAndResetRun == true)
                            {
                                sc.AddInfoMessage("3IP: method " + methodId + " it's first or reseted run");
                                try
                                {
                                    string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                    BlocksOfSecretSent.Add(partOfSecret);
                                    //add check for 0
                                    ip.Identification = Convert.ToUInt16(partOfSecret, 2);
                                    secret = secret.Remove(0, usedbits);
                                }
                                catch
                                {
                                    if (secret.Length != 0)
                                    {
                                        string transfer = (secret[0].Equals("0")) ? "0" : secret; //using rest, but it cant start with zero
                                        BlocksOfSecretSent.Add(transfer);
                                        ip.Identification = Convert.ToUInt16(transfer, 2); //using rest + padding removed .PadLeft(usedbits, '0')
                                        secret = secret.Remove(0, transfer.Length);
                                    }
                                    return new Tuple<IpV4Layer, string>(ip, secret); //nothing more          
                                }
                            }
                            break;
                        }
                    case 305: //IP flag (MF + offset) //SENDER
                        {
                            int usedbits = GetMethodCapacity(305); //offset can be from 0 to 8191
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);                                
                                ushort offset = Convert.ToUInt16(partOfSecret, 2);
                                if (offset % 8 == 0 && offset != 0)
                                {
                                    //add check for 0                                    
                                    sc.AddInfoMessage("3IP: method " + methodId); //mentioning when is actually used                                    
                                    BlocksOfSecretSent.Add(partOfSecret);
                                    ip.Fragmentation = new IpV4Fragmentation(IpV4FragmentationOptions.MoreFragments, offset); //also IpV4FragmentationOptions.DoNotFragment
                                    secret = secret.Remove(0, usedbits);
                                }
                                else
                                {
                                    ip.Fragmentation = new IpV4Fragmentation(IpV4FragmentationOptions.None, 0); //end of fragmentation                                     
                                }
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    ip.Fragmentation = new IpV4Fragmentation(IpV4FragmentationOptions.None, 0); //TODO could be deadlocked when no other method used
                                    string transfer = (secret[0].Equals("0")) ? "0" : secret; //using rest, but it cant start with zero                                    
                                    ushort offset = Convert.ToUInt16(transfer, 2);
                                    if (offset % 8 == 0 && offset != 0)
                                    {
                                        sc.AddInfoMessage("3IP: method " + methodId); //mentioning when is actually used
                                        BlocksOfSecretSent.Add(transfer);
                                        ip.Fragmentation = new IpV4Fragmentation(IpV4FragmentationOptions.MoreFragments, offset); //rewrite previous
                                        secret = secret.Remove(0, transfer.Length);
                                        return new Tuple<IpV4Layer, string>(ip, secret); //nothing more to send 
                                    }                                    
                                }
                                else
                                {
                                    ip.Fragmentation = new IpV4Fragmentation(IpV4FragmentationOptions.None, 0);
                                    return new Tuple<IpV4Layer, string>(ip, secret); //nothing more to send
                                }
                            }
                            break;
                        }
                }
            }

            //temp output to file
            //string separated = string.Join("-\n", BlocksOfSecretSent.ToArray());
            //string FilePath = System.AppDomain.CurrentDomain.BaseDirectory + "blocks-server.txt";
            //System.IO.File.AppendAllText(FilePath, separated.ToString());

            return new Tuple<IpV4Layer, string>(ip, secret);
        }
        public static string GetContent3Network(IpV4Datagram ip, List<int> stegoUsedMethodIds, NetReceiverServer rs = null) //RECEIVER
        {
            if (ip == null) { return null; } //extra protection
            List<string> BlocksOfSecret = new List<string>();

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case 301: //IP (Type of service / DiffServ agresive) //RECEIVER
                        {
                            rs.AddInfoMessage("3IP: method " + methodId); //add number of received bits in this iteration
                            string binvalue = Convert.ToString(ip.TypeOfService, 2); //use whole field
                            if (Int32.Parse(binvalue) == 0)
                            {
                                BlocksOfSecret.Add("0"); //when received zero's do not pad
                            }
                            else
                            {
                                BlocksOfSecret.Add(binvalue.PadLeft(GetMethodCapacity(methodId), '0')); //when zeros was cutted
                            }
                            break;
                        }
                    case 302: //IP (Type of service / DiffServ) //RECEIVER
                        {
                            rs.AddInfoMessage("3IP: method " + methodId); //add number of received bits in this iteration
                            string fullfield = Convert.ToString(ip.TypeOfService, 2).PadLeft(8, '0');
                            string binvalue = fullfield.Substring(fullfield.Length - 2); //use only last two bits

                            if (Int32.Parse(binvalue) == 0)
                            {
                                BlocksOfSecret.Add("0"); //when received zero's do not pad
                            }
                            else
                            {
                                BlocksOfSecret.Add(binvalue.PadLeft(GetMethodCapacity(methodId), '0')); //when zeros was cutted
                                //BlocksOfSecret.Add(binvalue);
                            }

                            break;
                        }
                    case 303: //IP (Identification) //RECEIVER
                        {
                            rs.AddInfoMessage("3IP: method " + methodId); //add number of received bits in this iteration

                            string binvalue = Convert.ToString(ip.Identification, 2);
                            if (Int32.Parse(binvalue) == 0)
                            {
                                if (Identification != binvalue) //do not add value when it didnt change
                                {
                                    Identification = binvalue;
                                    BlocksOfSecret.Add("0"); //do not pad
                                }
                            }
                            else
                            {
                                if (Identification != binvalue) //do not add value when it didnt change
                                {
                                    Identification = binvalue;
                                    BlocksOfSecret.Add(binvalue.PadLeft(GetMethodCapacity(methodId), '0')); //when zeros was cutted    
                                }
                            }
                            break;
                        }
                    case 305: //IP flag (MF + offset) //RECEIVER
                        {
                            if(ip.Fragmentation.Offset != 0) //DEBUG
                            {
                                rs.AddInfoMessage("3IP: method " + methodId + "offset now");
                            }

                            if (ip.Fragmentation.Options == IpV4FragmentationOptions.MoreFragments) 
                            {
                                rs.AddInfoMessage("3IP: method " + methodId);
                                string binvalue = Convert.ToString(ip.Fragmentation.Offset, 2);
                                if (Double.Parse(binvalue) == 0)
                                {
                                    BlocksOfSecret.Add("0");
                                }
                                else
                                {
                                    BlocksOfSecret.Add(binvalue.PadLeft(GetMethodCapacity(methodId), '0'));
                                }
                            }
                                
                            break;
                        }
                }
            }

            if (BlocksOfSecret.Count != 0) //providing value output
            {
                //temp output to file
                //string separated = string.Join("_", BlocksOfSecret.ToArray());
                //string FilePath = System.AppDomain.CurrentDomain.BaseDirectory + "blocks-client.txt";
                //System.IO.File.AppendAllText(FilePath, separated.ToString());

                return string.Join("", BlocksOfSecret.ToArray()); //joining binary substring
            }
            else
            {
                return null;
            }
        }

        //icmp layer methods
        public static Tuple<IcmpEchoLayer, string> SetContent3Icmp(IcmpEchoLayer icmp, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc = null)
        {
            if (icmp == null) { return null; } //extra protection            

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case IcmpGenericPing: //ICMP (standard, for other layers) //SENDER (alias 331, but value used in code)
                        {
                            sc.AddInfoMessage("3ICMP: method " + methodId);
                            icmp.SequenceNumber = SequenceNumber++; //legacy sequence number
                            icmp.Identifier = (ushort)rand.Next(0, 65535);
                            //add delay 1000 miliseconds on parent object
                            break;
                        }
                    case 333: //ICMP (Identifier) //SENDER
                        {
                            sc.AddInfoMessage("3ICMP: method " + methodId);
                            if (!stegoUsedMethodIds.Contains(335)) //do not overwrite sequence number when that method selected
                            {
                                icmp.SequenceNumber = SequenceNumber++; //legacy sequence number
                            }

                            const int usedbits = 16;
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                icmp.Identifier = Convert.ToUInt16(partOfSecret, 2);
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    //icmp.Identifier = Convert.ToUInt16(secret.PadLeft(usedbits, '0'), 2); //using rest + padding
                                    icmp.Identifier = Convert.ToUInt16(secret, 2); //using rest + padding
                                    secret = secret.Remove(0, secret.Length);
                                }
                                return new Tuple<IcmpEchoLayer, string>(icmp, secret); //nothing more                               
                            }
                            break;
                        }
                    case 335: //ICMP (Sequence number) //SENDER
                        {
                            sc.AddInfoMessage("3ICMP: method " + methodId);
                            if (!stegoUsedMethodIds.Contains(333)) //do not overwrite identifier when that method selected
                            {
                                icmp.Identifier = icmp.Identifier = (ushort)rand.Next(0, 65535); //legacy Identifier number
                            }

                            const int usedbits = 16;
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                icmp.SequenceNumber = Convert.ToUInt16(partOfSecret, 2);
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    icmp.SequenceNumber = Convert.ToUInt16(secret, 2); //using rest + padding .PadLeft(usedbits, '0')
                                    secret = secret.Remove(0, secret.Length);
                                }
                                return new Tuple<IcmpEchoLayer, string>(icmp, secret); //nothing more                               
                            }
                            break;
                        }
                    case 337:
                        {
                            //icmp.CodeValue = 1;
                            //TESTING METHOD
                            //should be nice to use optional data
                            break;

                        }
                }
            }

            return new Tuple<IcmpEchoLayer, string>(icmp, secret);
        }

        public static string GetContent3Icmp(IcmpEchoDatagram icmp, List<int> stegoUsedMethodIds, NetReceiverServer rs = null) //RECEIVER
        {
            if (icmp == null) { return null; } //extra protection
            List<string> BlocksOfSecret = new List<string>();

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case IcmpGenericPing: //ICMP (pure) RECEIVER
                        {
                            rs.AddInfoMessage("3ICMP: method " + methodId + " (no stehanography included)"); //add number of received bits in this iteration
                            break;
                        }
                    case 333: //ICMP (Identifier) RECEIVER
                        {
                            rs.AddInfoMessage("3ICMP: method " + methodId);
                            string binvalue = Convert.ToString(icmp.Identifier, 2);
                            BlocksOfSecret.Add(binvalue.PadLeft(16, '0')); //when zeros was cutted
                            break;
                        }
                    case 335: //ICMP (Sequence number) RECEIVER
                        {
                            rs.AddInfoMessage("3ICMP: method " + methodId);
                            string binvalue = Convert.ToString(icmp.SequenceNumber, 2);
                            BlocksOfSecret.Add(binvalue.PadLeft(16, '0')); //when zeros was cutted
                            break;
                        }
                }
            }

            if (BlocksOfSecret.Count != 0) //providing value output
            {
                return string.Join("", BlocksOfSecret.ToArray()); //joining binary substring
            }
            else
            {
                return null;
            }
        }

        //-L4------------------------------------------------------------------------------------------------------------------

        //udp layer methods - skipped

        //TCP layer methods
        public static Tuple<TcpLayer, string> SetContent4Tcp(TcpLayer tcp, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc = null)
        {
            if (tcp == null) { return null; } //extra protection

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                //needs to handle states of TCP to read only from proper state like SYN, SYNACK

                switch (methodId)
                {
                    case 451: //TCP (standard, for other layers) //SENDER
                        {
                            sc.AddInfoMessage("4TCP: method " + methodId);
                            break;
                        }
                    //453
                    case 455: //TCP Urgent pointer //SENDER
                        {
                            sc.AddInfoMessage("4TCP: method " + methodId);
                            //TODO implement
                            //tcp.ControlBits = tcp.TcpControlBits.Urgent;
                            //TODO tcp.UrgentPointer = 16 bit 
                            break;
                        }
                    case 457: //TCP
                        {
                            //display filter: tcp.options.time_stamp
                            //http://ithitman.blogspot.fi/2013/02/tcp-timestamp-demystified.html
                            //https://github.com/PcapDotNet/Pcap.Net/blob/master/PcapDotNet/src/PcapDotNet.Packets/Transport/TcpOptionTimestamp.cs

                            //tcp.TcpOptions = new TcpOptionTimestamp(555, 555);
                            break;
                        }
                }
            }

            return new Tuple<TcpLayer, string>(tcp, secret);
        }

        //TODO GetContent4Tcp

        //-L5-to-L7------------------------------------------------------------------------------------------------------------

        //L7-dns
        public static Tuple<DnsLayer, string> SetContent7Dns(DnsLayer dns, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc = null) //SENDER
        {
            if (dns == null) { return null; } //extra protection

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case 701: //DNS clean //SENDER
                        {
                            sc.AddInfoMessage("7DNS: legacy method " + methodId + " (no data removed)");
                            dns.Id = (ushort)rand.Next(0, 65535);
                            break;
                        }
                    case 703: //DNS (transaction id) //SENDER
                        {
                            sc.AddInfoMessage("7DNS: method " + methodId);
                            const int usedbits = 16;
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                dns.Id = Convert.ToUInt16(partOfSecret, 2);
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    dns.Id = Convert.ToUInt16(secret, 2); //using rest + padding .PadLeft(usedbits, '0')
                                    secret = secret.Remove(0, secret.Length);
                                }
                                return new Tuple<DnsLayer, string>(dns, secret); //nothing more          
                            }
                            break;
                        }
                    case 705:
                        {
                            DnsQueryResourceRecord dnsRequest = ((DnsQueryResourceRecord)dns.Queries.First());
                            string stegoInFormOfIpAddress = "255.255.255.255";
                            IpV4Address fakeIp = new IpV4Address(stegoInFormOfIpAddress);
                            dns.Answers.Add(new DnsDataResourceRecord(dnsRequest.DomainName, dnsRequest.DnsType, DnsClass.Internet, dnsRequest.Ttl, new DnsResourceDataIpV4(fakeIp)));
                            break;
                        }
                }
            }
            return new Tuple<DnsLayer, string>(dns, secret);
        }

        public static string GetContent7Dns(DnsDatagram dns, List<int> stegoUsedMethodIds, NetReceiverServer rs = null) //RECEIVER
        {
            if (dns == null) { return null; } //extra protection
            List<string> BlocksOfSecret = new List<string>();

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case 701: //DNS (pure) RECEIVER
                        {
                            rs.AddInfoMessage("7DNS: method " + methodId + " (no stehanography included)");
                            break;
                        }
                    case 703: //DNS (Id) RECEIVER
                        {
                            rs.AddInfoMessage("7DNS: method " + methodId);
                            string binvalue = Convert.ToString(dns.Id, 2);
                            BlocksOfSecret.Add(binvalue.PadLeft(16, '0')); //when zeros was cutted
                            break;
                        }
                    case 705:
                        {
                            //throw NotImplementedException;
                            //alisfliksajfkaf++;
                            break;
                        }
                }
            }

            if (BlocksOfSecret.Count != 0) //providing value output
            {
                return string.Join("", BlocksOfSecret.ToArray()); //joining binary substring
            }
            else
            {
                return null;
            }
        }


        //L7-http
        internal static Tuple<HttpLayer, string> SetContent7Http(HttpLayer http, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc)
        {
            if (http == null) { return null; } //extra protection

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case 731: //HTTP clean //SENDER
                        {
                            sc.AddInfoMessage("7HTTP: legacy method " + methodId + " (no data removed)");
                            break;
                        }
                    case 733: //HTTP GET facebook picture //SENDER
                        {
                            sc.AddInfoMessage("7HTTP: method " + methodId);
                            const int usedbits = 200;
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);

                                //request for FB image                                                                
                                //https://scontent-arn2-1.xx.fbcdn.net/v/t1.0-9/28783581_2044295905785459_4786842833526980608_o.jpg?_nc_cat=0&oh=f393eabff543c0014fa7d549a606cd72&oe=5B3439EE
                                //https://scontent-arn2-1.xx.fbcdn.net/v/t31.0-8/26756412_2019306211617762_7982736838983831551_o.jpg?_nc_cat=0&oh=1fe51c45d8053ae7b0f95570763e3cfd&oe=5B729DA3
                                //https://www.facebook.com/<username>/photos/a.1693236240891429.1073741827.1693231710891882/2042454925969557/?type=3&theater

                                //instagram                                
                                //https://scontent-arn2-1.cdninstagram.com/vp/8c1b8efbaac6831e2a15d59e6a047276/5B6D8993/t51.2885-15/e35/29417270_195771601230887_3084806938333020160_n.jpg
                                //https://scontent-arn2-1.cdninstagram.com/vp/d9d046183c558e2065c5c6b77ded7cd8/5B74D9C8/t51.2885-15/e35/29417740_596673960668154_5630438044696838144_n.jpg

                                //twitter
                                //https://pbs.twimg.com/media/DUj28AMXkAEy8q7.jpg:large
                                //https://pbs.twimg.com/media/DZ-5xFDU0AAJRPu.jpg:large

                                //pinterest


                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    //dns.Id = Convert.ToUInt16(secret.PadLeft(usedbits, '0'), 2); //using rest + padding
                                    secret = secret.Remove(0, secret.Length);
                                }
                                return new Tuple<HttpLayer, string>(http, secret); //nothing more                                         
                            }
                            break;
                        }
                }
            }
            return new Tuple<HttpLayer, string>(http, secret);
        }

        public static string GetContent7Http(HttpDatagram http, List<int> stegoUsedMethodIds, NetReceiverServer rs = null)
        {
            if (http == null) { return null; } //extra protection
            List<string> BlocksOfSecret = new List<string>();

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                switch (methodId)
                {
                    case 731: //HTTP (pure) RECEIVER
                        {
                            rs.AddInfoMessage("7HTTP: method " + methodId);

                            break;
                        }
                }
            }

            if (BlocksOfSecret.Count != 0) //providing value output
            {
                return string.Join("", BlocksOfSecret.ToArray()); //joining binary substring
            }
            else
            {
                return null;
            }
        }
    }
}

