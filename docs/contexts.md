# Контексти (Bounded Contexts) та агрегати

| Контекст | База | Технологія | Агрегати / сутності-власники |
|---|---|---|---|
| **Замовлення** (Orders) | OrdersDb (SQL Server) | ADO.NET + Dapper, збережувані процедури | Customer (+CustomerProfile 1:1), **Order** (+OrderItem, OrderStatusHistory, Payment), Promotion; локальна копія довідника Products |
| **Каталог** (Catalog) | CatalogDb (SQL Server) | EF Core Code First | **Product** (+ProductDetail 1:1, ProductCategory M:N), Publisher, Category |
| **Відгуки** (Reviews) | ReviewsDb (MongoDB) | MongoDB Driver, Clean Architecture + CQRS | **Review** (+Comment, Author VO, Rating VO), **Discussion** (+DiscussionMessage), ProductRating |

## Правила належності

* Книга (`Product`) належить лише Каталогу. Інші контексти знають її за `ProductId` і зберігають знімок/копію окремих полів.
* Клієнт і замовлення належать лише контексту Замовлень. Відгуки знають автора лише за `userId` + `displayName` (значення з токена користувача).
* Між базами немає FOREIGN KEY і немає JOIN-ів; цілісність `ProductId` перевіряє прикладний код (сервіс Замовлень звіряється з локальним довідником `Products`).

## Агрегати та життєвий цикл

* **Order = Order + OrderItem + OrderStatusHistory + Payment**: позиції не існують без замовлення; статус змінюється лише процедурою `usp_Order_ChangeStatus` за матрицею переходів `New → Confirmed → Paid → Shipped → Completed` (+ `Cancelled` з New/Confirmed/Paid).
* **Product = Product + ProductDetail + ProductCategory**: деталі видаляються разом із товаром (cascade); запас змінюється методами `AddStock/RemoveStock`.
* **Review = Review + Comment[]**: коментарі вбудовані (їх небагато, читаються разом з відгуком); **Discussion** — гібрид: останні 3 повідомлення вбудовані, повна історія — у `discussionMessages`.
