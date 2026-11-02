using Json.Models;
using Json.Repositories;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics.Contracts;
using System.Text;

namespace Json.Services
{
    public class OrderValidator
    {
        private readonly CustomerRepository _customerRepository;
        private readonly ProductRepository _productRepository;
        private readonly CouponRepository _couponRepository;
        private readonly OrderRepository _orderRepository;

        public OrderValidator(CustomerRepository customerRepository, ProductRepository productRepository,
            CouponRepository couponRepository, OrderRepository orderRepository)
        {
            _customerRepository = customerRepository;
            _productRepository = productRepository;
            _couponRepository = couponRepository;
            _orderRepository = orderRepository;
        }

        public async Task ValidateOrderAsync(Order order, CancellationToken token)
        {
            string? Failler;
            Failler = await ValidateCustomerAsync(order, token);

            if(Failler is null)
                Failler = await ValidateCouponAsync(order, token);

            if(Failler is null)
                Failler = await ValidateProductsAsync(order, token);

            if (Failler is null)
            {
                order.Status = OrderStatus.Validated;
                order.FailureReason = null;
            }

            else
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = Failler;
            }
            await _orderRepository.SaveAsync(order,token);
        }
        

        public async Task<string?> ValidateCustomerAsync(Order order,CancellationToken token)
        {
            var customer = await _customerRepository.GetByIdAsync(order.CustomerId,token);
            if(customer is null)
            {
                return $"The Customer {order.CustomerId} Not Found";
            }
            if (!customer.IsActive)
            {
                return $"The Customer {order.CustomerId} Not Active";
            }
            return null;
        }

        public async Task<string?> ValidateCouponAsync(Order order, CancellationToken token)
        {
            if(order.CouponCode is not null)
            {
                var coupon = await _couponRepository.GetByCodeAsync(order.CouponCode, token);
                if(coupon is null)
                {
                    return $"The Coupon {order.CouponCode} Not Exsit";
                }
                if (coupon.ExpiryDate.Date < DateTime.Today)
                {
                    return $"The Coupon {order.CouponCode} Finsh Date";
                }
            }
            return null;
        }
        
        public async Task<string?>ValidateProductsAsync(Order order, CancellationToken token)
        {
            foreach(var  item in order.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId, token);
                if(product is null)
                {
                    return $"The Product {item.ProductId} Not Exsit";
               
                }
                if(product.StockQuantity < item.Quantity)
                {
                    return $"Insufficient stock for '{product.Name}'";
            
                }
            }
            return null;
        }



        public void ValidateOrder(Order order)
        {
            string? failureReason = null;

            var customer = _customerRepository.GetById(order.CustomerId);

            if (customer is null)
            {
                failureReason = $"Customer #{order.CustomerId} not found";
            }
            else if (!customer.IsActive)
            {
                failureReason = "Customer is inactive";
            }

            if (failureReason is null)
            {
                foreach (var item in order.Items)
                {
                    var product = _productRepository.GetById(item.ProductId);

                    if (product is null)
                    {
                        failureReason = $"Product #{item.ProductId} not found";
                        break;
                    }

                    if (product.StockQuantity < item.Quantity)
                    {
                        failureReason =
                            $"Insufficient stock for '{product.Name}'";
                        break;
                    }
                }
            }

            if (failureReason is null && order.CouponCode is not null)
            {
                var coupon = _couponRepository.GetByCode(order.CouponCode);

                if (coupon is null)
                {
                    failureReason =
                        $"Coupon '{order.CouponCode}' does not exist";
                }
                else if (coupon.ExpiryDate.Date < DateTime.Today)
                {
                    failureReason =
                        $"Coupon '{order.CouponCode}' has expired";
                }
            }

            if (failureReason is null)
            {
                order.Status = OrderStatus.Validated;
                order.FailureReason = null;
            }
            else
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = failureReason;
            }

        }
    }
}
