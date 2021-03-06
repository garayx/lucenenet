﻿/* 
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 * 
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.IO;
using System.Linq;
using Lucene.Net.Documents;
using Lucene.Net.Support;
using NUnit.Framework;

using WhitespaceAnalyzer = Lucene.Net.Analysis.WhitespaceAnalyzer;
using Document = Lucene.Net.Documents.Document;
using Field = Lucene.Net.Documents.Field;
using Directory = Lucene.Net.Store.Directory;
using FSDirectory = Lucene.Net.Store.FSDirectory;
using CompressionTools = Lucene.Net.Documents.CompressionTools;
using IndexSearcher = Lucene.Net.Search.IndexSearcher;
using ScoreDoc = Lucene.Net.Search.ScoreDoc;
using TermQuery = Lucene.Net.Search.TermQuery;
using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;
using _TestUtil = Lucene.Net.Util._TestUtil;
using Lucene.Net.Util;

namespace Lucene.Net.Index
{
	
	/*
	Verify we can read the pre-2.1 file format, do searches
	against it, and add documents to it.*/
	
	[TestFixture]
	public class TestBackwardsCompatibility:LuceneTestCase
	{
		
		// Uncomment these cases & run them on an older Lucene
		// version, to generate an index to test backwards
		// compatibility.  Then, cd to build/test/index.cfs and
		// run "zip index.<VERSION>.cfs.zip *"; cd to
		// build/test/index.nocfs and run "zip
		// index.<VERSION>.nocfs.zip *".  Then move those 2 zip
		// files to your trunk checkout and add them to the
		// oldNames array.
		
		/*
		public void testCreatePreLocklessCFS() throws IOException {
		createIndex("index.cfs", true);
		}
		
		public void testCreatePreLocklessNoCFS() throws IOException {
		createIndex("index.nocfs", false);
		}
		*/

        public class AnonymousFieldSelector : FieldSelector
        {
            public FieldSelectorResult Accept(string fieldName)
            {
                return ("compressed".Equals(fieldName)) ? FieldSelectorResult.SIZE : FieldSelectorResult.LOAD;
            }
        }

		
		/* Unzips dirName + ".zip" --> dirName, removing dirName
		first */
		public virtual void  Unzip(System.String zipName, System.String destDirName)
		{
#if SHARP_ZIP_LIB
			// get zip input stream
			ICSharpCode.SharpZipLib.Zip.ZipInputStream zipFile;
			zipFile = new ICSharpCode.SharpZipLib.Zip.ZipInputStream(System.IO.File.OpenRead(zipName + ".zip"));

			// get dest directory name
			System.String dirName = FullDir(destDirName);
			System.IO.FileInfo fileDir = new System.IO.FileInfo(dirName);

			// clean up old directory (if there) and create new directory
			RmDir(fileDir.FullName);
			System.IO.Directory.CreateDirectory(fileDir.FullName);

			// copy file entries from zip stream to directory
			ICSharpCode.SharpZipLib.Zip.ZipEntry entry;
			while ((entry = zipFile.GetNextEntry()) != null)
			{
                System.IO.Stream streamout = new System.IO.BufferedStream(new System.IO.FileStream(new System.IO.FileInfo(System.IO.Path.Combine(fileDir.FullName, entry.Name)).FullName, System.IO.FileMode.Create));
                byte[] buffer = new byte[8192];
				int len;
				while ((len = zipFile.Read(buffer, 0, buffer.Length)) > 0)
				{
					streamout.Write(buffer, 0, len);
				}

                streamout.Close();
			}

            zipFile.Close();
#else
			Assert.Fail("Needs integration with SharpZipLib");
#endif
            }
		
		[Test]
		public virtual void  TestCreateCFS()
		{
			System.String dirName = "testindex.cfs";
			CreateIndex(dirName, true);
			RmDir(dirName);
		}
		
		[Test]
		public virtual void  TestCreateNoCFS()
		{
			System.String dirName = "testindex.nocfs";
			CreateIndex(dirName, true);
			RmDir(dirName);
		}

	    internal string[] oldNames = new []
	                                            {
	                                                "19.cfs", "19.nocfs", "20.cfs", "20.nocfs", "21.cfs", "21.nocfs", "22.cfs", 
                                                    "22.nocfs", "23.cfs", "23.nocfs", "24.cfs", "24.nocfs", "29.cfs",
	                                                "29.nocfs"
	                                            };

        private void assertCompressedFields29(Directory dir, bool shouldStillBeCompressed)
        {
            int count = 0;
            int TEXT_PLAIN_LENGTH = TEXT_TO_COMPRESS.Length*2;
            // FieldSelectorResult.SIZE returns 2*number_of_chars for String fields:
            int BINARY_PLAIN_LENGTH = BINARY_TO_COMPRESS.Length;

            IndexReader reader = IndexReader.Open(dir, true, null);
            try
            {
                // look into sub readers and check if raw merge is on/off
                var readers = new System.Collections.Generic.List<IndexReader>();
                ReaderUtil.GatherSubReaders(readers, reader);
                foreach (IndexReader ir in readers)
                {
                    FieldsReader fr = ((SegmentReader) ir).GetFieldsReader(null);
                    Assert.IsTrue(shouldStillBeCompressed != fr.CanReadRawDocs(),
                                  "for a 2.9 index, FieldsReader.canReadRawDocs() must be false and other way round for a trunk index");
                }

                // test that decompression works correctly
                for (int i = 0; i < reader.MaxDoc; i++)
                {
                    if (!reader.IsDeleted(i))
                    {
                        Document d = reader.Document(i, null);
                        if (d.Get("content3", null) != null) continue;
                        count++;
                        IFieldable compressed = d.GetFieldable("compressed");
                        if (int.Parse(d.Get("id", null))%2 == 0)
                        {
                            Assert.IsFalse(compressed.IsBinary);
                            Assert.AreEqual(TEXT_TO_COMPRESS, compressed.StringValue(null),
                                            "incorrectly decompressed string");
                        }
                        else
                        {
                            Assert.IsTrue(compressed.IsBinary);
                            Assert.IsTrue(BINARY_TO_COMPRESS.SequenceEqual(compressed.GetBinaryValue(null)),
                                          "incorrectly decompressed binary");
                        }
                    }
                }

                //check if field was decompressed after optimize
                for (int i = 0; i < reader.MaxDoc; i++)
                {
                    if (!reader.IsDeleted(i))
                    {
                        Document d = reader.Document(i, new AnonymousFieldSelector(), null);
                        if (d.Get("content3", null) != null) continue;
                        count++;
                        // read the size from the binary value using BinaryReader (this prevents us from doing the shift ops ourselves):
                        // ugh, Java uses Big-Endian streams, so we need to do it manually.
                        byte[] encodedSize = d.GetFieldable("compressed").GetBinaryValue(null).Take(4).Reverse().ToArray();
                        int actualSize = BitConverter.ToInt32(encodedSize, 0);
                        int compressedSize = int.Parse(d.Get("compressedSize", null));
                        bool binary = int.Parse(d.Get("id", null))%2 > 0;
                        int shouldSize = shouldStillBeCompressed
                                             ? compressedSize
                                             : (binary ? BINARY_PLAIN_LENGTH : TEXT_PLAIN_LENGTH);
                        Assert.AreEqual(shouldSize, actualSize, "size incorrect");
                        if (!shouldStillBeCompressed)
                        {
                            Assert.IsFalse(compressedSize == actualSize,
                                           "uncompressed field should have another size than recorded in index");
                        }
                    }
                }
                Assert.AreEqual(34*2, count, "correct number of tests");
            }
            finally
            {
                reader.Dispose();
            }
        }

	    [Test]
		public virtual void  TestOptimizeOldIndex()
	    {
	        int hasTested29 = 0;
			for (int i = 0; i < oldNames.Length; i++)
			{
				System.String dirName = Paths.CombinePath(Paths.ProjectRootDirectory, "test/Lucene.Net.Test/index/index." + oldNames[i]);
				Unzip(dirName, oldNames[i]);
				System.String fullPath = FullDir(oldNames[i]);
				Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(fullPath));

                if (oldNames[i].StartsWith("29."))
                {
                    assertCompressedFields29(dir, true);
                    hasTested29++;
                }

				IndexWriter w = new IndexWriter(dir, new WhitespaceAnalyzer(), IndexWriter.MaxFieldLength.LIMITED, null);
				w.Optimize(null);
				w.Close();
				
				_TestUtil.CheckIndex(dir);

                if (oldNames[i].StartsWith("29."))
                {
                    assertCompressedFields29(dir, false);
                    hasTested29++;
                }

				dir.Close();
				RmDir(oldNames[i]);
			}
            Assert.AreEqual(4, hasTested29, "test for compressed field should have run 4 times");
		}
		
		[Test]
		public virtual void  TestSearchOldIndex()
		{
			for (int i = 0; i < oldNames.Length; i++)
			{
                System.String dirName = Paths.CombinePath(Paths.ProjectRootDirectory, "test/Lucene.Net.Test/index/index." + oldNames[i]);
				Unzip(dirName, oldNames[i]);
				searchIndex(oldNames[i], oldNames[i]);
				RmDir(oldNames[i]);
			}
		}
		
		[Test]
		public virtual void  TestIndexOldIndexNoAdds()
		{
			for (int i = 0; i < oldNames.Length; i++)
			{
                System.String dirName = Paths.CombinePath(Paths.ProjectRootDirectory, "test/Lucene.Net.Test/index/index." + oldNames[i]);
				Unzip(dirName, oldNames[i]);
				ChangeIndexNoAdds(oldNames[i]);
				RmDir(oldNames[i]);
			}
		}
		
		[Test]
		public virtual void  TestIndexOldIndex()
		{
			for (int i = 0; i < oldNames.Length; i++)
			{
                System.String dirName = Paths.CombinePath(Paths.ProjectRootDirectory, "test/Lucene.Net.Test/index/index." + oldNames[i]);
				Unzip(dirName, oldNames[i]);
				ChangeIndexWithAdds(oldNames[i]);
				RmDir(oldNames[i]);
			}
		}
		
		private void  TestHits(ScoreDoc[] hits, int expectedCount, IndexReader reader)
		{
			int hitCount = hits.Length;
			Assert.AreEqual(expectedCount, hitCount, "wrong number of hits");
			for (int i = 0; i < hitCount; i++)
			{
				reader.Document(hits[i].Doc, null);
				reader.GetTermFreqVectors(hits[i].Doc, null);
			}
		}
		
		public virtual void  searchIndex(System.String dirName, System.String oldName)
		{
			//QueryParser parser = new QueryParser("contents", new WhitespaceAnalyzer());
			//Query query = parser.parse("handle:1");
			
			dirName = FullDir(dirName);
			
			Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(dirName));
			IndexSearcher searcher = new IndexSearcher(dir, true, null);
			IndexReader reader = searcher.IndexReader;
			
			_TestUtil.CheckIndex(dir);
			
			for (int i = 0; i < 35; i++)
			{
				if (!reader.IsDeleted(i))
				{
					Document d = reader.Document(i, null);
					var fields = d.GetFields();
					if (!oldName.StartsWith("19.") && !oldName.StartsWith("20.") && !oldName.StartsWith("21.") && !oldName.StartsWith("22."))
					{
						if (d.GetField("content3") == null)
						{
						    int numFields = oldName.StartsWith("29.") ? 7 : 5;
							Assert.AreEqual(numFields, fields.Count);
							Field f = d.GetField("id");
							Assert.AreEqual("" + i, f.StringValue(null));
							
							f = (Field) d.GetField("utf8");
							Assert.AreEqual("Lu\uD834\uDD1Ece\uD834\uDD60ne \u0000 \u2620 ab\ud917\udc17cd", f.StringValue(null));
							
							f = (Field) d.GetField("autf8");
							Assert.AreEqual("Lu\uD834\uDD1Ece\uD834\uDD60ne \u0000 \u2620 ab\ud917\udc17cd", f.StringValue(null));
							
							f = (Field) d.GetField("content2");
							Assert.AreEqual("here is more content with aaa aaa aaa", f.StringValue(null));
							
							f = (Field) d.GetField("fie\u2C77ld");
							Assert.AreEqual("field with non-ascii name", f.StringValue(null));
						}
					}
				}
				// Only ID 7 is deleted
				else
					Assert.AreEqual(7, i);
			}
			
			ScoreDoc[] hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			
			// First document should be #21 since it's norm was
			// increased:
			Document d2 = searcher.Doc(hits[0].Doc, null);
			Assert.AreEqual("21", d2.Get("id", null), "didn't get the right document first");
			
			TestHits(hits, 34, searcher.IndexReader);
			
			if (!oldName.StartsWith("19.") && !oldName.StartsWith("20.") && !oldName.StartsWith("21.") && !oldName.StartsWith("22."))
			{
				// Test on indices >= 2.3
				hits = searcher.Search(new TermQuery(new Term("utf8", "\u0000")), null, 1000, null).ScoreDocs;
				Assert.AreEqual(34, hits.Length);
				hits = searcher.Search(new TermQuery(new Term("utf8", "Lu\uD834\uDD1Ece\uD834\uDD60ne")), null, 1000, null).ScoreDocs;
				Assert.AreEqual(34, hits.Length);
				hits = searcher.Search(new TermQuery(new Term("utf8", "ab\ud917\udc17cd")), null, 1000, null).ScoreDocs;
				Assert.AreEqual(34, hits.Length);
			}
			
			searcher.Close();
			dir.Close();
		}
		
		private int Compare(System.String name, System.String v)
		{
			int v0 = System.Int32.Parse(name.Substring(0, (2) - (0)));
			int v1 = System.Int32.Parse(v);
			return v0 - v1;
		}
		
		/* Open pre-lockless index, add docs, do a delete &
		* setNorm, and search */
		public virtual void  ChangeIndexWithAdds(System.String dirName)
		{
			System.String origDirName = dirName;
			dirName = FullDir(dirName);
			
			Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(dirName));
			
			// open writer
			IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, IndexWriter.MaxFieldLength.UNLIMITED, null);
			
			// add 10 docs
			for (int i = 0; i < 10; i++)
			{
				AddDoc(writer, 35 + i);
			}
			
			// make sure writer sees right total -- writer seems not to know about deletes in .del?
			int expected;
			if (Compare(origDirName, "24") < 0)
			{
				expected = 45;
			}
			else
			{
				expected = 46;
			}
			Assert.AreEqual(expected, writer.MaxDoc(), "wrong doc count");
			writer.Close();
			
			// make sure searching sees right # hits
			IndexSearcher searcher = new IndexSearcher(dir, true, null);
			ScoreDoc[] hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			Document d = searcher.Doc(hits[0].Doc, null);
			Assert.AreEqual("21", d.Get("id", null), "wrong first document");
			TestHits(hits, 44, searcher.IndexReader);
			searcher.Close();
			
			// make sure we can do delete & setNorm against this
			// pre-lockless segment:
			IndexReader reader = IndexReader.Open(dir, false, null);
			Term searchTerm = new Term("id", "6");
			int delCount = reader.DeleteDocuments(searchTerm, null);
			Assert.AreEqual(1, delCount, "wrong delete count");
			reader.SetNorm(22, "content", (float) 2.0, null);
			reader.Close();
			
			// make sure they "took":
			searcher = new IndexSearcher(dir, true, null);
			hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			Assert.AreEqual(43, hits.Length, "wrong number of hits");
			d = searcher.Doc(hits[0].Doc, null);
			Assert.AreEqual("22", d.Get("id", null), "wrong first document");
			TestHits(hits, 43, searcher.IndexReader);
			searcher.Close();
			
			// optimize
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, IndexWriter.MaxFieldLength.UNLIMITED, null);
			writer.Optimize(null);
			writer.Close();
			
			searcher = new IndexSearcher(dir, true, null);
			hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			Assert.AreEqual(43, hits.Length, "wrong number of hits");
			d = searcher.Doc(hits[0].Doc, null);
			TestHits(hits, 43, searcher.IndexReader);
			Assert.AreEqual("22", d.Get("id", null), "wrong first document");
			searcher.Close();
			
			dir.Close();
		}
		
		/* Open pre-lockless index, add docs, do a delete &
		* setNorm, and search */
		public virtual void  ChangeIndexNoAdds(System.String dirName)
		{
			dirName = FullDir(dirName);
			
			Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(dirName));
			
			// make sure searching sees right # hits
			IndexSearcher searcher = new IndexSearcher(dir, true, null);
			ScoreDoc[] hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			Assert.AreEqual(34, hits.Length, "wrong number of hits");
			Document d = searcher.Doc(hits[0].Doc, null);
			Assert.AreEqual("21", d.Get("id", null), "wrong first document");
			searcher.Close();
			
			// make sure we can do a delete & setNorm against this
			// pre-lockless segment:
			IndexReader reader = IndexReader.Open(dir, false, null);
			Term searchTerm = new Term("id", "6");
			int delCount = reader.DeleteDocuments(searchTerm, null);
			Assert.AreEqual(1, delCount, "wrong delete count");
			reader.SetNorm(22, "content", (float) 2.0, null);
			reader.Close();
			
			// make sure they "took":
			searcher = new IndexSearcher(dir, true, null);
			hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			Assert.AreEqual(33, hits.Length, "wrong number of hits");
			d = searcher.Doc(hits[0].Doc, null);
			Assert.AreEqual("22", d.Get("id", null), "wrong first document");
			TestHits(hits, 33, searcher.IndexReader);
			searcher.Close();
			
			// optimize
			IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), false, IndexWriter.MaxFieldLength.UNLIMITED, null);
			writer.Optimize(null);
			writer.Close();
			
			searcher = new IndexSearcher(dir, true, null);
			hits = searcher.Search(new TermQuery(new Term("content", "aaa")), null, 1000, null).ScoreDocs;
			Assert.AreEqual(33, hits.Length, "wrong number of hits");
			d = searcher.Doc(hits[0].Doc, null);
			Assert.AreEqual("22", d.Get("id", null), "wrong first document");
			TestHits(hits, 33, searcher.IndexReader);
			searcher.Close();
			
			dir.Close();
		}
		
		public virtual void  CreateIndex(System.String dirName, bool doCFS)
		{
			
			RmDir(dirName);
			
			dirName = FullDir(dirName);
			
			Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(dirName));
			IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true, IndexWriter.MaxFieldLength.LIMITED, null);
			writer.UseCompoundFile = doCFS;
			writer.SetMaxBufferedDocs(10);
			
			for (int i = 0; i < 35; i++)
			{
				AddDoc(writer, i);
			}
			Assert.AreEqual(35, writer.MaxDoc(), "wrong doc count");
			writer.Close();
			
			// open fresh writer so we get no prx file in the added segment
			writer = new IndexWriter(dir, new WhitespaceAnalyzer(), IndexWriter.MaxFieldLength.LIMITED, null);
			writer.UseCompoundFile = doCFS;
			writer.SetMaxBufferedDocs(10);
			AddNoProxDoc(writer);
			writer.Close();
			
			// Delete one doc so we get a .del file:
		    IndexReader reader = IndexReader.Open(dir, false, null);
			Term searchTerm = new Term("id", "7");
			int delCount = reader.DeleteDocuments(searchTerm, null);
			Assert.AreEqual(1, delCount, "didn't delete the right number of documents");
			
			// Set one norm so we get a .s0 file:
			reader.SetNorm(21, "content", (float) 1.5, null);
			reader.Close();
		}
		
		/* Verifies that the expected file names were produced */

        [Test]
        public virtual void TestExactFileNames()
        {
            System.String outputDir = "lucene.backwardscompat0.index";
            RmDir(outputDir);

            try
            {
                Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(FullDir(outputDir)));

                IndexWriter writer = new IndexWriter(dir, new WhitespaceAnalyzer(), true,
                                                     IndexWriter.MaxFieldLength.UNLIMITED, null);
                writer.SetRAMBufferSizeMB(16.0);
                for (int i = 0; i < 35; i++)
                {
                    AddDoc(writer, i);
                }
                Assert.AreEqual(35, writer.MaxDoc(), "wrong doc count");
                writer.Close();

                // Delete one doc so we get a .del file:
                IndexReader reader = IndexReader.Open(dir, false, null);
                Term searchTerm = new Term("id", "7");
                int delCount = reader.DeleteDocuments(searchTerm, null);
                Assert.AreEqual(1, delCount, "didn't delete the right number of documents");

                // Set one norm so we get a .s0 file:
                reader.SetNorm(21, "content", (float) 1.5, null);
                reader.Close();

                // The numbering of fields can vary depending on which
                // JRE is in use.  On some JREs we see content bound to
                // field 0; on others, field 1.  So, here we have to
                // figure out which field number corresponds to
                // "content", and then set our expected file names below
                // accordingly:
                CompoundFileReader cfsReader = new CompoundFileReader(dir, "_0.cfs", null);
                FieldInfos fieldInfos = new FieldInfos(cfsReader, "_0.fnm", null);
                int contentFieldIndex = -1;
                for (int i = 0; i < fieldInfos.Size(); i++)
                {
                    FieldInfo fi = fieldInfos.FieldInfo(i);
                    if (fi.name_ForNUnit.Equals("content"))
                    {
                        contentFieldIndex = i;
                        break;
                    }
                }
                cfsReader.Close();
                Assert.IsTrue(contentFieldIndex != -1,
                              "could not locate the 'content' field number in the _2.cfs segment");

                // Now verify file names:
                System.String[] expected;
                expected = new System.String[]
                               {"_0.cfs", "_0_1.del", "_0_1.s" + contentFieldIndex, "segments_3", "segments.gen"};

                System.String[] actual = dir.ListAll(null);
                System.Array.Sort(expected);
                System.Array.Sort(actual);
                if (!CollectionsHelper.Equals(expected, actual))
                {
                    Assert.Fail("incorrect filenames in index: expected:\n    " + AsString(expected) +
                                "\n  actual:\n    " + AsString(actual));
                }
                dir.Close();
            }
            finally
            {
                RmDir(outputDir);
            }
        }

	    private System.String AsString(System.String[] l)
		{
			System.String s = "";
			for (int i = 0; i < l.Length; i++)
			{
				if (i > 0)
				{
					s += "\n    ";
				}
				s += l[i];
			}
			return s;
		}
		
		private void  AddDoc(IndexWriter writer, int id)
		{
			Document doc = new Document();
			doc.Add(new Field("content", "aaa", Field.Store.NO, Field.Index.ANALYZED));
			doc.Add(new Field("id", System.Convert.ToString(id), Field.Store.YES, Field.Index.NOT_ANALYZED));
			doc.Add(new Field("autf8", "Lu\uD834\uDD1Ece\uD834\uDD60ne \u0000 \u2620 ab\ud917\udc17cd", Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.WITH_POSITIONS_OFFSETS));
			doc.Add(new Field("utf8", "Lu\uD834\uDD1Ece\uD834\uDD60ne \u0000 \u2620 ab\ud917\udc17cd", Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.WITH_POSITIONS_OFFSETS));
			doc.Add(new Field("content2", "here is more content with aaa aaa aaa", Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.WITH_POSITIONS_OFFSETS));
			doc.Add(new Field("fie\u2C77ld", "field with non-ascii name", Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.WITH_POSITIONS_OFFSETS));
            /* This was used in 2.9 to generate an index with compressed field:
			if (id % 2 == 0)
			{
				doc.Add(new Field("compressed", TEXT_TO_COMPRESS, Field.Store.COMPRESS, Field.Index.NOT_ANALYZED));
				doc.Add(new Field("compressedSize", System.Convert.ToString(TEXT_COMPRESSED_LENGTH), Field.Store.YES, Field.Index.NOT_ANALYZED));
			}
			else
			{
				doc.Add(new Field("compressed", BINARY_TO_COMPRESS, Field.Store.COMPRESS));
				doc.Add(new Field("compressedSize", System.Convert.ToString(BINARY_COMPRESSED_LENGTH), Field.Store.YES, Field.Index.NOT_ANALYZED));
			}*/
            // Add numeric fields, to test if flex preserves encoding
		    doc.Add(new NumericField("trieInt", 4).SetIntValue(id));
            doc.Add(new NumericField("trieLong", 4).SetLongValue(id));
			writer.AddDocument(doc, null);
		}
		
		private void  AddNoProxDoc(IndexWriter writer)
		{
			Document doc = new Document();
			Field f = new Field("content3", "aaa", Field.Store.YES, Field.Index.ANALYZED);
			f.OmitTermFreqAndPositions = true;
			doc.Add(f);
			f = new Field("content4", "aaa", Field.Store.YES, Field.Index.NO);
			f.OmitTermFreqAndPositions = true;
			doc.Add(f);
			writer.AddDocument(doc, null);
		}
		
		private void  RmDir(System.String dir)
		{
			System.IO.FileInfo fileDir = new System.IO.FileInfo(FullDir(dir));
			bool tmpBool;
			if (System.IO.File.Exists(fileDir.FullName))
				tmpBool = true;
			else
				tmpBool = System.IO.Directory.Exists(fileDir.FullName);
			if (tmpBool)
			{
				System.IO.FileInfo[] files = FileSupport.GetFiles(fileDir);
				if (files != null)
				{
					for (int i = 0; i < files.Length; i++)
					{
						bool tmpBool2;
						if (System.IO.File.Exists(files[i].FullName))
						{
							System.IO.File.Delete(files[i].FullName);
							tmpBool2 = true;
						}
						else if (System.IO.Directory.Exists(files[i].FullName))
						{
							System.IO.Directory.Delete(files[i].FullName);
							tmpBool2 = true;
						}
						else
							tmpBool2 = false;
						bool generatedAux = tmpBool2;
					}
				}
				bool tmpBool3;
				if (System.IO.File.Exists(fileDir.FullName))
				{
					System.IO.File.Delete(fileDir.FullName);
					tmpBool3 = true;
				}
				else if (System.IO.Directory.Exists(fileDir.FullName))
				{
					System.IO.Directory.Delete(fileDir.FullName);
					tmpBool3 = true;
				}
				else
					tmpBool3 = false;
				bool generatedAux2 = tmpBool3;
			}
		}
		
		public static System.String FullDir(System.String dirName)
		{
			return new System.IO.FileInfo(System.IO.Path.Combine(AppSettings.Get("tempDir", Path.GetTempPath()), dirName)).FullName;
		}
		
		internal const System.String TEXT_TO_COMPRESS = "this is a compressed field and should appear in 3.0 as an uncompressed field after merge";
		// FieldSelectorResult.SIZE returns compressed size for compressed fields,
		// which are internally handled as binary;
		// do it in the same way like FieldsWriter, do not use
		// CompressionTools.compressString() for compressed fields:
		internal static readonly byte[] BINARY_TO_COMPRESS = new byte[]{1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20};

        /* This was used in 2.9 to generate an index with compressed field
        internal static int TEXT_COMPRESSED_LENGTH;
        internal static readonly int BINARY_COMPRESSED_LENGTH = CompressionTools.Compress(BINARY_TO_COMPRESS).Length;
        static TestBackwardsCompatibility()
        {
            {
                try
                {
                    TEXT_COMPRESSED_LENGTH = CompressionTools.Compress(System.Text.Encoding.GetEncoding("UTF-8").GetBytes(TEXT_TO_COMPRESS)).Length;
                }
                catch (System.Exception e)
                {
                    throw new System.SystemException();
                }
            }
        }*/
	}
}