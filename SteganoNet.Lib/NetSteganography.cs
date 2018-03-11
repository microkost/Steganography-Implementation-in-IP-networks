﻿using System;
using System.Collections.Generic;
using System.Linq;
using PcapDotNet.Packets.Icmp;
using PcapDotNet.Packets.IpV4;

namespace SteganoNetLib
{
    public static class NetSteganography //not static
    {
        //magic numbers dialer (never use numbers directly)
        public const int IpRangeStart = 300;
        public const int IpRangeEnd = 329;
        public const int IcmpRangeStart = 330;
        public const int IcmpRangeEnd = 359;
        public const int NetworkRangeStart = IpRangeStart;
        public const int NetworkRangeEnd = 399;

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
             */

            //for details read file MethodDescription.txt, keep it updated if changing following list!
            Dictionary<int, string> listOfStegoMethods = new Dictionary<int, string>
            {
                // { 000, "Nothing, pure" },
                { 301, "IP (Type of service / DiffServ agresive) - 8b" },
                { 302, "IP (Type of service / DiffServ) - 2b" },
                { 303, "IP (Identification)" },
                { 305, "IP (Flags)" },
                { 331, "ICMP (standard, for other layers) - 0b" },
                { 333, "ICMP (Identifier)" },
                { 335, "ICMP (Sequence number)" }
            };

            //IP method 1 - most transparent - using Identification field and changing it every two minutes accoring to standard - iteration of value 
            //IP method X - offset number like TTL lower, smth constant is under or value is unmasked... IF allowed!
            //IP method 2 - maximum method (method 1 + usage of flags + fragment offset + 
            //ip method 3 - transparent - count TTL and use some value under as rest...
            //IP method 4 - TypeOfService fild - extrely lame way but... Usage high bits 6 + 7 is "OK"...
            //IP method 5  - 

            return listOfStegoMethods; //DO NOT MODIFY THAT LIST DURING RUNNING
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
        public static string GetContent3Network(IpV4Datagram ip, List<int> stegoUsedMethodIds, NetReceiverServer rs = null) //RECEIVER
        {
            if (ip == null) { return null; } //extra protection
            List<string> BlocksOfSecret = new List<string>();



            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                rs.AddInfoMessage("3IP: method " + methodId); //add number of received bits in this iteration
                switch (methodId)
                {
                    case 301: //IP (Type of service / DiffServ agresive) RECEIVER
                        {
                            string binvalue = Convert.ToString(ip.TypeOfService, 2); //use whole field
                            string binvaluePadded = binvalue.PadLeft(8, '0');
                            BlocksOfSecret.Add(binvaluePadded); //when zeros was cutted
                            break;
                        }
                    case 302: //IP (Type of service / DiffServ) RECEIVER
                        {
                            string fullfield = Convert.ToString(ip.TypeOfService, 2).PadLeft(8, '0');
                            string binvalue = fullfield.Substring(fullfield.Length - 2); //use only last two bits
                            BlocksOfSecret.Add(binvalue);
                            break;
                        }
                    case 303: //IP (Identification) RECEIVER
                        {
                            string binvalue = Convert.ToString(ip.Identification, 2);
                            //TODO, only in first packet!
                            BlocksOfSecret.Add(binvalue.PadLeft(16, '0')); //when zeros was cutted
                            break;
                        }
                    case 331: //ICMP (pure) RECEIVER
                        {

                            break;
                        }
                    case 333: //ICMP (Identifier) RECEIVER
                        {
                            IcmpIdentifiedDatagram icmp = (ip.Icmp.IsValid == true) ? (IcmpIdentifiedDatagram)ip.Icmp : null; //parsing layer for processing            
                            if (icmp.IsValid != true)
                                continue;

                            string binvalue = Convert.ToString(icmp.Identifier, 2);
                            BlocksOfSecret.Add(binvalue.PadLeft(16, '0')); //when zeros was cutted

                            break;
                        }
                    case 335: //ICMP (Sequence number) RECEIVER
                        {
                            IcmpIdentifiedDatagram icmp = (ip.Icmp.IsValid == true) ? (IcmpIdentifiedDatagram)ip.Icmp : null; //parsing layer for processing            
                            if (icmp.IsValid != true)
                                continue;

                            //todo

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

        //SENDER
        public static Tuple<IpV4Layer, string> SetContent3Network(IpV4Layer ip, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc = null)
        {
            if (ip == null) { return null; } //extra protection

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                sc.AddInfoMessage("3IP: method " + methodId);

                switch (methodId)
                {
                    case 301: //IP (Type of service / DiffServ) //SENDER
                        {
                            const int usedbits = 8;
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                //sc.AddInfoMessage(">> " + methodId + " : " + partOfSecret);
                                ip.TypeOfService = Convert.ToByte(partOfSecret, 2); //using 8 bits                                
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    ip.TypeOfService = Convert.ToByte(secret.PadLeft(usedbits, '0'), 2); //using rest + padding
                                    //sc.AddInfoMessage(">> " + methodId + " : " + secret + " alias " + secret.PadLeft(usedbits, '0'));
                                    secret = secret.Remove(0, secret.Length);
                                }
                                return new Tuple<IpV4Layer, string>(ip, secret); //nothing more                               
                            }
                            break;
                        }
                    case 302: //IP (Type of service / DiffServ) //SENDER
                        {
                            const int usedbits = 2;
                            try
                            {
                                string partOfSecret = secret.Remove(usedbits, secret.Length - usedbits);
                                //sc.AddInfoMessage(">> " + methodId + " : " + partOfSecret);
                                ip.TypeOfService = Convert.ToByte(partOfSecret, 2);
                                secret = secret.Remove(0, usedbits);
                            }
                            catch
                            {
                                if (secret.Length != 0)
                                {
                                    ip.TypeOfService = Convert.ToByte(secret.PadLeft(usedbits, '0'), 2); //using rest + padding
                                    //sc.AddInfoMessage(">> " + methodId + " : " + secret + " alias " + secret.PadLeft(usedbits, '0'));
                                    secret = secret.Remove(0, secret.Length);
                                }
                                return new Tuple<IpV4Layer, string>(ip, secret); //nothing more          
                            }
                            break;
                        }
                    case 303: //IP (Identification) //SENDER
                        {
                            //https://tools.ietf.org/html/rfc6864#page-4
                            //first run start timer, add value to output and save it outside loop. Replace value and send new one after timer expire.
                            break;
                        }
                }
            }

            return new Tuple<IpV4Layer, string>(ip, secret);
        }

        public static Tuple<IcmpLayer, string> SetContent3Icmp(IcmpLayer icmp, List<int> stegoUsedMethodIds, string secret, NetSenderClient sc = null)
        {
            if (icmp == null) { return null; } //extra protection

            //{ 331, "ICMP (standard, for other layers) - 0b" },
            //{ 333, "ICMP (Identifier)" },
            //{ 335, "ICMP (Sequence number)" }

            foreach (int methodId in stegoUsedMethodIds) //process every method separately on this packet
            {
                sc.AddInfoMessage("3IP: method " + methodId);

                switch (methodId)
                {
                    case 331: //ICMP (standard, for other layers) //SENDER
                        {
                            break;
                        }
                }
            }


            return new Tuple<IcmpLayer, string>(icmp, secret);
        }

        //tcp layer methods

        //udp layer methods - skipped by assigment

        //application layer methods
    }
}
