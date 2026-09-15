# Table bloat caused by bulk inserts

[This](https://www.madeiradata.com/post/high-allocated-unused-space) blog describes scenario where table can get bigger than necessary. The main cause is doing small inserts using bulk insert. Since I encountered this myself, I wanted to have 100% confirmation that this is the case. Using Claude Code I build a reproducer for it. It comprises of 2 files:

[setup.sql](https://github.com/Tisit/tisit.github.io/blob/trunk/TableBloat/setup.sql)

[Run.cs](https://github.com/Tisit/tisit.github.io/blob/trunk/TableBloat/Run.cs)

To reproduce you need to put these files in the same directory and run in it (.NET 10 required):

````
dotnet run Run.cs -- --server InstanceName
````

Afterwards You will get results like these:
>--- SqlBulkCopy  batch=1            (small inserts)
>    reserved =    157.2 MB     used =    19.7 MB     (64s)
>
>--- SqlBulkCopy  batch=1000         (remediation)
>    reserved =     22.9 MB     used =    19.7 MB     (1s)
>
>--- SqlBulkCopy  batch=1 + TF 692   (remediation)
>    reserved =     19.7 MB     used =    19.7 MB     (57s)

As you can see using batch size of 1 eats up much more space than strictly required. MS documentation that I found seems to suggest this behavior is only applicable to SIMPLE and BULK LOAD recovery moder. This is wrong. I have seen this under FULL recovery model too.

## Final thoughts

I observed this behavior in a live system. Since I started with suspicion what was going on, I decided to use Claude to write me a reproducer. It was very quick to create working code confirming my hypothesis. The code itself required debloating, since Claude added many unnecessary steps. But I still think it is a nice tool for cases like this.
